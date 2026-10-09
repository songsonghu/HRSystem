using HRSystem.Application.Common;
using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using HRSystem.Domain.Entities;
using HRSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRSystem.Application.Services;

/// <summary>
/// Orchestrates the account request workflow:
/// draft -> (self-service: manager approval) -> dispatch to responsible departments ->
/// assignees complete items -> ledger updated -> master status recomputed.
/// </summary>
public class AccountRequestService : IAccountRequestService
{
    private static readonly RequestType[] InServiceTypes = { RequestType.Add, RequestType.Remove };

    private readonly IAppDbContext _db;
    private readonly WorkflowService _workflow;
    private readonly IEmailService _email;
    private readonly IFileStorageService _files;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IUserDirectoryService _users;
    private readonly ILogger<AccountRequestService> _logger;

    public AccountRequestService(
        IAppDbContext db,
        WorkflowService workflow,
        IEmailService email,
        IFileStorageService files,
        ICurrentUser currentUser,
        IAuditService audit,
        IUserDirectoryService users,
        ILogger<AccountRequestService> logger)
    {
        _db = db;
        _workflow = workflow;
        _email = email;
        _files = files;
        _currentUser = currentUser;
        _audit = audit;
        _users = users;
        _logger = logger;
    }

    // ------------------------------------------------------------------ access

    /// <summary>The current user as the workflow sees them.</summary>
    private sealed record Actor(string? UserId, bool IsHr, bool IsAdmin, bool CanOnboard, bool CanDepart, int? EmployeeId, IReadOnlyList<int> ManagedDepartmentIds)
    {
        public bool Manages(Employee employee)
            => employee.DepartmentId is int d && ManagedDepartmentIds.Contains(d);

        public bool CanRaiseFor(Employee employee)
            => IsHr || employee.Id == EmployeeId || Manages(employee);

        /// <summary>An employee requesting for themself, without HR or manager rights over themself.</summary>
        public bool NeedsApproval(Employee employee)
            => !IsHr && employee.Id == EmployeeId && !Manages(employee);

        public bool CanSee(AccountRequest r)
            => IsHr || IsAdmin
               || r.EmployeeId == EmployeeId
               || (UserId is not null && (r.CreatedBy == UserId || r.ApproverUserId == UserId
                                          || r.Items.Any(i => i.AssignedUserId == UserId)))
               || (r.Employee is not null && Manages(r.Employee));

        public bool CanSubmit(AccountRequest r)
            => r.Status == RequestStatus.Draft && (IsHr || (UserId is not null && r.CreatedBy == UserId));

        public bool CanApprove(AccountRequest r)
            => r.Status == RequestStatus.PendingApproval && (IsAdmin || (UserId is not null && r.ApproverUserId == UserId));

        public bool CanUpload(AccountRequest r)
            => IsHr || (UserId is not null && r.CreatedBy == UserId
                        && r.Status is RequestStatus.Draft or RequestStatus.PendingApproval);
    }

    private async Task<Actor> GetActorAsync(CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        int? employeeId = userId is null
            ? null
            : await _db.Employees.Where(e => e.UserId == userId).Select(e => (int?)e.Id).FirstOrDefaultAsync(ct);
        var managed = employeeId is null
            ? new List<int>()
            : await _db.Departments.Where(d => d.ManagerEmployeeId == employeeId).Select(d => d.Id).ToListAsync(ct);

        return new Actor(userId, _currentUser.HasPermission(Permissions.AccountRequestsManage),
            _currentUser.IsAdmin, _currentUser.HasPermission(Permissions.OnboardingManage),
            _currentUser.HasPermission(Permissions.DeparturesManage), employeeId, managed);
    }

    // ------------------------------------------------------------------ create

    public async Task<RequestCreateContextDto> GetCreateContextAsync(CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);

        var query = _db.Employees.AsNoTracking();
        if (!actor.IsHr)
            query = query.Where(e => e.Id == actor.EmployeeId
                                     || (e.DepartmentId != null && actor.ManagedDepartmentIds.Contains(e.DepartmentId.Value)));

        return new RequestCreateContextDto
        {
            Employees = await query
                .OrderBy(e => e.Name)
                .Select(e => new EmployeeOptionDto(e.Id, e.Name + " (" + e.EmployeeNo + ")"))
                .ToListAsync(ct),
            // Offboard requests are raised by the departure workflow, not by hand.
            RequestTypes = actor.IsHr ? new[] { RequestType.Onboard, RequestType.Add, RequestType.Remove } : InServiceTypes
        };
    }

    public async Task<Result<int>> CreateAsync(CreateRequestDto dto, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);

        // Onboarding/departure staff may raise the account request that accompanies their checklist.
        bool checklistRequest = (dto.RequestType == RequestType.Onboard && actor.CanOnboard)
                                || (dto.RequestType == RequestType.Offboard && actor.CanDepart);

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == dto.EmployeeId, ct);
        if (employee is null || !(checklistRequest || actor.CanRaiseFor(employee))) return Result<int>.Fail("Employee not found.");
        if (!actor.IsHr && !checklistRequest && !InServiceTypes.Contains(dto.RequestType))
            return Result<int>.Fail("Only HR can raise onboarding or offboarding requests.");
        if (dto.AccountTypeIds.Count == 0) return Result<int>.Fail("Select at least one account type.");

        // Account types printed on this employee's requisition form (Staff or AE/Sales/SA); removals
        // may also target any account the employee currently holds, even one outside that form.
        var audience = employee.Category.ToAudience();
        var isRemoval = dto.RequestType is RequestType.Remove or RequestType.Offboard;
        var heldTypeIds = isRemoval
            ? await _db.EmployeeAccounts
                .Where(a => a.EmployeeId == employee.Id && a.Status == AccountStatus.Active)
                .Select(a => a.AccountTypeId)
                .ToListAsync(ct)
            : new List<int>();
        var accountTypes = await _db.AccountTypes
            .Where(a => dto.AccountTypeIds.Contains(a.Id)
                        && ((a.IsActive && (a.Audience == AccountTypeAudience.Both || a.Audience == audience))
                            || heldTypeIds.Contains(a.Id)))
            .ToListAsync(ct);

        if (accountTypes.Count == 0) return Result<int>.Fail("No valid account types selected.");
        if (accountTypes.Count != dto.AccountTypeIds.Distinct().Count())
            return Result<int>.Fail("One or more selected account types are not available for this employee's requisition form.");

        var request = new AccountRequest
        {
            // Final number is derived from the identity after insert, so it is unique without counting.
            RequestNo = $"TMP-{Guid.NewGuid():N}"[..16],
            EmployeeId = employee.Id,
            RequestType = dto.RequestType,
            Status = RequestStatus.Draft,
            Remark = dto.Remark,
            IsNewHeadcount = dto.IsNewHeadcount,
            ReplacementOf = dto.ReplacementOf,
            LastDay = dto.LastDay,
            CreatedBy = actor.UserId
        };

        // Fan out: one item per account type, pre-assigned to the responsible dept.
        foreach (var at in accountTypes)
        {
            request.Items.Add(new AccountRequestItem
            {
                AccountTypeId = at.Id,
                AssignedDeptId = at.ResponsibleDeptId,
                Status = ItemStatus.NotStarted,
                RequestDetail = dto.Details.TryGetValue(at.Id, out var detail) ? detail : null
            });
        }

        _db.AccountRequests.Add(request);
        await _db.SaveChangesAsync(ct);
        request.RequestNo = $"REQ-{DateTime.UtcNow.Year}-{request.Id:D5}";
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("CreateRequest", nameof(AccountRequest), request.Id.ToString(), request.RequestNo, ct);
        return Result<int>.Success(request.Id);
    }

    // ------------------------------------------------------- submit / approve

    public async Task<Result> SubmitAsync(int requestId, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);

        return await ConcurrencyRetry.RunAsync(_db, async () =>
        {
            var request = await LoadForDispatchAsync(requestId, ct);
            if (request is null || !actor.CanSee(request)) return Result.Fail("Request not found.");
            if (!actor.CanSubmit(request)) return Result.Fail("Only draft requests can be submitted, by HR or whoever created them.");
            if (request.Items.Count == 0) return Result.Fail("Request has no items.");

            var routingError = CheckRouting(request);
            if (routingError is not null) return Result.Fail(routingError);

            request.AppliedBy = actor.UserId;
            request.AppliedAt = DateTime.UtcNow;

            if (actor.NeedsApproval(request.Employee!))
            {
                var approverUserId = request.Employee!.Department?.Manager?.UserId;
                if (approverUserId is null)
                    return Result.Fail("Your department has no manager with a login account to approve this request. Please contact HR.");

                request.Status = RequestStatus.PendingApproval;
                request.ApproverUserId = approverUserId;
                await _db.SaveChangesAsync(ct);

                await NotifyApproverAsync(request, ct);
                await _audit.LogAsync("SubmitRequestForApproval", nameof(AccountRequest), request.Id.ToString(), request.RequestNo, ct);
                return Result.Success();
            }

            await DispatchAsync(request, ct);
            await _audit.LogAsync("SubmitRequest", nameof(AccountRequest), request.Id.ToString(), request.RequestNo, ct);
            return Result.Success();
        });
    }

    public async Task<Result> ApproveAsync(int requestId, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);

        return await ConcurrencyRetry.RunAsync(_db, async () =>
        {
            var request = await LoadForDispatchAsync(requestId, ct);
            if (request is null || !actor.CanSee(request)) return Result.Fail("Request not found.");
            if (!actor.CanApprove(request)) return Result.Fail("This request is not waiting for your approval.");

            var routingError = CheckRouting(request);
            if (routingError is not null) return Result.Fail(routingError);

            request.DecidedBy = actor.UserId;
            request.DecidedAt = DateTime.UtcNow;
            await DispatchAsync(request, ct);

            await _audit.LogAsync("ApproveRequest", nameof(AccountRequest), request.Id.ToString(), request.RequestNo, ct);
            return Result.Success();
        });
    }

    public async Task<Result> RejectAsync(int requestId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Result.Fail("Please give a reason for rejecting.");
        var actor = await GetActorAsync(ct);

        return await ConcurrencyRetry.RunAsync(_db, async () =>
        {
            var request = await _db.AccountRequests
                .Include(r => r.Employee)
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == requestId, ct);
            if (request is null || !actor.CanSee(request)) return Result.Fail("Request not found.");
            if (!actor.CanApprove(request)) return Result.Fail("This request is not waiting for your approval.");

            request.Status = RequestStatus.Rejected;
            request.DecidedBy = actor.UserId;
            request.DecidedAt = DateTime.UtcNow;
            request.DecisionRemark = reason.Trim();
            await _db.SaveChangesAsync(ct);

            await NotifyRequesterOfRejectionAsync(request, ct);
            await _audit.LogAsync("RejectRequest", nameof(AccountRequest), request.Id.ToString(), reason, ct);
            return Result.Success();
        });
    }

    private Task<AccountRequest?> LoadForDispatchAsync(int requestId, CancellationToken ct)
        => _db.AccountRequests
            .Include(r => r.Employee).ThenInclude(e => e!.Department).ThenInclude(d => d!.Manager)
            .Include(r => r.Items).ThenInclude(i => i.AccountType)
            .Include(r => r.Items).ThenInclude(i => i.AssignedDept).ThenInclude(d => d!.Manager)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);

    private static string? CheckRouting(AccountRequest request)
    {
        var unrouted = request.Items
            .Where(i => i.AssignedDept?.Manager?.UserId is null)
            .Select(i => i.AssignedDept?.Name ?? $"#{i.AssignedDeptId}")
            .Distinct()
            .ToList();
        return unrouted.Count == 0
            ? null
            : $"No manager with a login account is set for: {string.Join(", ", unrouted)}. Set one under Departments first.";
    }

    /// <summary>Assign every item to its department manager (snapshot) and notify everyone.</summary>
    private async Task DispatchAsync(AccountRequest request, CancellationToken ct)
    {
        foreach (var item in request.Items)
            item.AssignedUserId = item.AssignedDept!.Manager!.UserId;

        request.Status = RequestStatus.Submitted;
        await _db.SaveChangesAsync(ct);

        NotifyEmployee(request);
        await NotifyDepartmentManagersAsync(request, ct);
        _logger.LogInformation("Request {RequestNo} dispatched with {Count} items.", request.RequestNo, request.Items.Count);
    }

    // ------------------------------------------------------------------ read

    public async Task<RequestDto?> GetAsync(int requestId, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        var r = await _db.AccountRequests.AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Items).ThenInclude(i => i.AccountType)
            .Include(x => x.Items).ThenInclude(i => i.AssignedDept)
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == requestId, ct);
        if (r is null || !actor.CanSee(r)) return null;

        var dto = MapRequest(r);
        dto.CanSubmit = actor.CanSubmit(r);
        dto.CanApprove = actor.CanApprove(r);
        dto.CanUpload = actor.CanUpload(r);
        dto.SubmitNeedsApproval = dto.CanSubmit && r.Employee is not null && actor.NeedsApproval(r.Employee);
        dto.ApproverName = await UserNameAsync(r.ApproverUserId, ct);
        dto.DecidedByName = await UserNameAsync(r.DecidedBy, ct);
        return dto;
    }

    public async Task<IReadOnlyList<RequestDto>> GetListAsync(CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        var query = _db.AccountRequests.AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Items).ThenInclude(i => i.AccountType)
            .Include(x => x.Items).ThenInclude(i => i.AssignedDept)
            .AsQueryable();

        if (!actor.IsHr && !actor.IsAdmin)
        {
            var uid = actor.UserId;
            var empId = actor.EmployeeId;
            var managed = actor.ManagedDepartmentIds;
            query = query.Where(r => r.EmployeeId == empId
                                     || r.CreatedBy == uid
                                     || r.ApproverUserId == uid
                                     || (r.Employee!.DepartmentId != null && managed.Contains(r.Employee.DepartmentId.Value)));
        }

        var list = await query.OrderByDescending(x => x.Id).ToListAsync(ct);
        return list.Select(MapRequest).ToList();
    }

    public async Task<IReadOnlyList<RequestDto>> GetPendingApprovalsAsync(CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        if (userId is null) return Array.Empty<RequestDto>();

        var list = await _db.AccountRequests.AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Items).ThenInclude(i => i.AccountType)
            .Include(x => x.Items).ThenInclude(i => i.AssignedDept)
            .Where(r => r.Status == RequestStatus.PendingApproval && r.ApproverUserId == userId)
            .OrderBy(r => r.AppliedAt)
            .ToListAsync(ct);
        return list.Select(MapRequest).ToList();
    }

    public async Task<IReadOnlyList<RequestItemDto>> GetMyPendingItemsAsync(CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId)) return Array.Empty<RequestItemDto>();

        var items = await _db.AccountRequestItems.AsNoTracking()
            .Include(i => i.AccountType)
            .Include(i => i.AssignedDept)
            .Include(i => i.Request).ThenInclude(r => r!.Employee)
            .Where(i => i.AssignedUserId == userId
                        && i.Status != ItemStatus.Completed
                        && i.Status != ItemStatus.Rejected
                        && (i.Request!.Status == RequestStatus.Submitted || i.Request.Status == RequestStatus.InProgress))
            .OrderBy(i => i.RequestId)
            .ToListAsync(ct);

        return items.Select(MapItem).ToList();
    }

    // ------------------------------------------------------- department work

    public async Task<Result> UpdateItemAsync(UpdateItemDto dto, CancellationToken ct = default)
    {
        return await ConcurrencyRetry.RunAsync(_db, async () =>
        {
            var item = await _db.AccountRequestItems
                .Include(i => i.Request)
                .FirstOrDefaultAsync(i => i.Id == dto.ItemId, ct);

            if (item is null) return Result.Fail("Request item not found.");
            if (!_currentUser.IsAdmin && (item.AssignedUserId is null || item.AssignedUserId != _currentUser.UserId))
                return Result.Fail("This item is not assigned to you.");
            if (item.Request!.Status is not (RequestStatus.Submitted or RequestStatus.InProgress or RequestStatus.Completed))
                return Result.Fail("This request has not been dispatched.");

            try
            {
                _workflow.EnsureItemTransition(item.Status, dto.Status);
            }
            catch (InvalidOperationException ex)
            {
                return Result.Fail(ex.Message);
            }

            item.Status = dto.Status;
            item.AccountValue = dto.AccountValue;
            item.ResultRemark = dto.ResultRemark;
            item.HandledBy = _currentUser.UserId;
            item.HandledAt = DateTime.UtcNow;

            if (dto.Status == ItemStatus.Completed)
                await ApplyToLedgerAsync(item, ct);

            // Recompute the master status from all items. The request row is always written
            // so its row version catches a parallel update by another department; the retry
            // then re-reads the items that department just changed.
            var siblings = await _db.AccountRequestItems.Where(i => i.RequestId == item.RequestId).ToListAsync(ct);
            var newStatus = _workflow.EvaluateRequestStatus(siblings);
            item.Request.Status = newStatus;
            item.Request.ModifiedAt = DateTime.UtcNow;
            if (newStatus == RequestStatus.Completed && item.Request.CompletedAt is null)
                item.Request.CompletedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync("UpdateItem", nameof(AccountRequestItem), item.Id.ToString(), $"{dto.Status}", ct);
            return Result.Success();
        });
    }

    public async Task<IReadOnlyList<AccountTypeOptionDto>> GetAccountTypeOptionsAsync(int employeeId, CancellationToken ct = default)
    {
        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null) return Array.Empty<AccountTypeOptionDto>();

        var audience = employee.Category.ToAudience();
        return await _db.AccountTypes.AsNoTracking()
            .Where(a => a.IsActive && (a.Audience == AccountTypeAudience.Both || a.Audience == audience))
            .OrderBy(a => a.SortOrder)
            .Select(a => new AccountTypeOptionDto
            {
                Id = a.Id,
                Name = a.Name,
                DeptId = a.ResponsibleDeptId,
                DeptName = a.ResponsibleDept!.Name,
                SortOrder = a.SortOrder,
                RequiresDetail = a.RequiresDetail,
                DetailLabel = a.DetailLabel
            })
            .ToListAsync(ct);
    }

    public async Task<Result> AddAttachmentAsync(
        int requestId, Stream content, string fileName, string? contentType, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        var request = await _db.AccountRequests
            .Include(r => r.Employee)
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null || !actor.CanSee(request)) return Result.Fail("Request not found.");
        if (!actor.CanUpload(request)) return Result.Fail("You cannot add attachments to this request.");

        var stored = await _files.SaveAsync(content, fileName, contentType, ct);
        _db.Attachments.Add(new Attachment
        {
            RequestId = requestId,
            FileName = stored.FileName,
            FilePath = stored.RelativePath,
            ContentType = stored.ContentType,
            FileSize = stored.Size,
            CreatedBy = actor.UserId
        });

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("AddAttachment", nameof(AccountRequest), requestId.ToString(), stored.FileName, ct);
        return Result.Success();
    }

    // ----------------------------------------------------------------- helpers

    /// <summary>
    /// Apply a completed item to the employee account ledger. For Onboard/Add
    /// it inserts or reactivates an Active account; for Remove/Offboard it
    /// disables the matching account.
    /// </summary>
    private async Task ApplyToLedgerAsync(AccountRequestItem item, CancellationToken ct)
    {
        var request = item.Request!;
        var isRemoval = request.RequestType is RequestType.Remove or RequestType.Offboard;

        var ledger = await _db.EmployeeAccounts
            .FirstOrDefaultAsync(a => a.EmployeeId == request.EmployeeId
                                      && a.AccountTypeId == item.AccountTypeId, ct);

        if (isRemoval)
        {
            if (ledger is not null && ledger.Status == AccountStatus.Active)
            {
                ledger.Status = AccountStatus.Disabled;
                ledger.DisabledAt = DateTime.UtcNow;
                ledger.SourceRequestItemId = item.Id;
            }
        }
        else
        {
            if (ledger is null)
            {
                _db.EmployeeAccounts.Add(new EmployeeAccount
                {
                    EmployeeId = request.EmployeeId,
                    AccountTypeId = item.AccountTypeId,
                    AccountValue = item.AccountValue,
                    Status = AccountStatus.Active,
                    OpenedAt = DateTime.UtcNow,
                    SourceRequestItemId = item.Id
                });
            }
            else
            {
                ledger.Status = AccountStatus.Active;
                ledger.AccountValue = item.AccountValue;
                ledger.DisabledAt = null;
                ledger.SourceRequestItemId = item.Id;
            }
        }
    }

    private async Task<string?> UserNameAsync(string? userId, CancellationToken ct)
        => userId is null ? null : (await _users.GetByIdAsync(userId, ct))?.UserName;

    /// <summary>Tell the target employee their request is being processed.</summary>
    private void NotifyEmployee(AccountRequest request)
    {
        var to = request.Employee?.Email;
        if (string.IsNullOrWhiteSpace(to)) return;

        var subject = request.RequestType == RequestType.Onboard
            ? $"Welcome aboard - account setup in progress ({request.RequestNo})"
            : $"Your account request is being processed ({request.RequestNo})";

        var body = $"""
            <p>Dear {request.Employee?.Name},</p>
            <p>Your account request <b>{request.RequestNo}</b> has been sent to the relevant departments
            for processing. You will be notified once it is completed.</p>
            <p>Regards,<br/>HR System</p>
            """;

        _email.Enqueue(new EmailMessage(new[] { to }, subject, body));
    }

    private async Task NotifyApproverAsync(AccountRequest request, CancellationToken ct)
    {
        var to = (await _users.GetByIdAsync(request.ApproverUserId!, ct))?.Email;
        if (string.IsNullOrWhiteSpace(to)) return;

        var types = string.Join(", ", request.Items.Select(i => i.AccountType?.Name));
        var subject = $"[Approval Required] Account request from {request.Employee?.Name} ({request.RequestNo})";
        var body = $"""
            <p>Hello,</p>
            <p><b>{request.Employee?.Name}</b> (No. {request.Employee?.EmployeeNo}) has requested:</p>
            <p><b>{request.RequestType}</b>: {types}</p>
            <p>Please log in to the HR System and approve or reject request {request.RequestNo}.</p>
            <p>Regards,<br/>HR System</p>
            """;
        _email.Enqueue(new EmailMessage(new[] { to }, subject, body));
    }

    private async Task NotifyRequesterOfRejectionAsync(AccountRequest request, CancellationToken ct)
    {
        var to = request.AppliedBy is null ? null : (await _users.GetByIdAsync(request.AppliedBy, ct))?.Email;
        to ??= request.Employee?.Email;
        if (string.IsNullOrWhiteSpace(to)) return;

        var subject = $"Your account request was not approved ({request.RequestNo})";
        var body = $"""
            <p>Dear {request.Employee?.Name},</p>
            <p>Your account request <b>{request.RequestNo}</b> was not approved.</p>
            <p>Reason: {System.Net.WebUtility.HtmlEncode(request.DecisionRemark)}</p>
            <p>Regards,<br/>HR System</p>
            """;
        _email.Enqueue(new EmailMessage(new[] { to }, subject, body));
    }

    /// <summary>
    /// Notify each assigned department manager, grouped so a manager who owns
    /// several account types receives a single consolidated email.
    /// </summary>
    private async Task NotifyDepartmentManagersAsync(AccountRequest request, CancellationToken ct)
    {
        var groups = request.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.AssignedUserId))
            .GroupBy(i => i.AssignedUserId!);

        foreach (var group in groups)
        {
            var managerEmail = (await _users.GetByIdAsync(group.Key, ct))?.Email;
            if (string.IsNullOrWhiteSpace(managerEmail)) continue;

            var typeList = string.Join(", ", group.Select(i => i.AccountType?.Name));
            var subject = $"[Action Required] Account provisioning for {request.Employee?.Name} ({request.RequestNo})";
            var body = $"""
                <p>Hello,</p>
                <p>Please log in to the HR System to process the following account type(s) for
                <b>{request.Employee?.Name}</b> (No. {request.Employee?.EmployeeNo}):</p>
                <p><b>{typeList}</b></p>
                <p>Request: {request.RequestNo} &middot; Type: {request.RequestType}</p>
                <p>Regards,<br/>HR System</p>
                """;

            _email.Enqueue(new EmailMessage(new[] { managerEmail }, subject, body));
        }
    }

    private static RequestDto MapRequest(AccountRequest r) => new()
    {
        Id = r.Id,
        RequestNo = r.RequestNo,
        EmployeeId = r.EmployeeId,
        EmployeeName = r.Employee?.Name ?? string.Empty,
        EmployeeNo = r.Employee?.EmployeeNo ?? string.Empty,
        EmployeeCategory = r.Employee?.Category ?? EmployeeCategory.Staff,
        RequestType = r.RequestType,
        Status = r.Status,
        Remark = r.Remark,
        IsNewHeadcount = r.IsNewHeadcount,
        ReplacementOf = r.ReplacementOf,
        LastDay = r.LastDay,
        AppliedAt = r.AppliedAt,
        CompletedAt = r.CompletedAt,
        DecidedAt = r.DecidedAt,
        DecisionRemark = r.DecisionRemark,
        Items = r.Items.Select(MapItem).ToList(),
        Attachments = r.Attachments.Select(a => new AttachmentDto
        {
            Id = a.Id, FileName = a.FileName, FileSize = a.FileSize
        }).ToList()
    };

    private static RequestItemDto MapItem(AccountRequestItem i) => new()
    {
        Id = i.Id,
        RequestId = i.RequestId,
        RequestNo = i.Request?.RequestNo ?? string.Empty,
        EmployeeName = i.Request?.Employee?.Name ?? string.Empty,
        EmployeeNo = i.Request?.Employee?.EmployeeNo ?? string.Empty,
        RequestType = i.Request?.RequestType ?? default,
        AccountTypeId = i.AccountTypeId,
        AccountTypeName = i.AccountType?.Name ?? string.Empty,
        AssignedDeptId = i.AssignedDeptId,
        AssignedDeptName = i.AssignedDept?.Name ?? string.Empty,
        Status = i.Status,
        AccountValue = i.AccountValue,
        RequestDetail = i.RequestDetail,
        ResultRemark = i.ResultRemark,
        HandledAt = i.HandledAt
    };
}
