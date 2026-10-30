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
/// department handlers complete items -> department approvers approve them (ledger updated) ->
/// master status recomputed.
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
    private readonly IAssignmentService _assignments;
    private readonly ILogger<AccountRequestService> _logger;

    public AccountRequestService(
        IAppDbContext db,
        WorkflowService workflow,
        IEmailService email,
        IFileStorageService files,
        ICurrentUser currentUser,
        IAuditService audit,
        IUserDirectoryService users,
        IAssignmentService assignments,
        ILogger<AccountRequestService> logger)
    {
        _db = db;
        _workflow = workflow;
        _email = email;
        _files = files;
        _currentUser = currentUser;
        _audit = audit;
        _users = users;
        _assignments = assignments;
        _logger = logger;
    }

    // ------------------------------------------------------------------ access

    /// <summary>The current user as the workflow sees them.</summary>
    private sealed record Actor(string? UserId, bool IsHr, bool IsAdmin, bool CanOnboard, bool CanDepart, int? EmployeeId,
        IReadOnlyList<int> ManagedDepartmentIds, IReadOnlyList<int> HandledDepartmentIds, IReadOnlyList<int> ApprovedDepartmentIds)
    {
        public bool Handles(AccountRequestItem item) => HandledDepartmentIds.Contains(item.AssignedDeptId);
        public bool Approves(AccountRequestItem item) => ApprovedDepartmentIds.Contains(item.AssignedDeptId);

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
                                          || r.Items.Any(i => Handles(i) || Approves(i))))
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
            _currentUser.HasPermission(Permissions.DeparturesManage), employeeId, managed,
            await _assignments.GetDepartmentIdsAsync(userId, WorkArea.Account, AssignmentRole.Handler, ct),
            await _assignments.GetDepartmentIdsAsync(userId, WorkArea.Account, AssignmentRole.Approver, ct));
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

            var routingError = await CheckRoutingAsync(request, ct);
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

            var routingError = await CheckRoutingAsync(request, ct);
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

    private Task<string?> CheckRoutingAsync(AccountRequest request, CancellationToken ct)
        => _assignments.CheckCoverageAsync(request.Items.Select(i => i.AssignedDeptId), WorkArea.Account, ct);

    /// <summary>Send the items to their departments' handlers and notify everyone.</summary>
    private async Task DispatchAsync(AccountRequest request, CancellationToken ct)
    {
        request.Status = RequestStatus.Submitted;
        await _db.SaveChangesAsync(ct);

        NotifyEmployee(request);
        await NotifyHandlersAsync(request, ct);
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

        var dto = MapRequest(r, actor);
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
        return list.Select(r => MapRequest(r, null)).ToList();
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
        return list.Select(r => MapRequest(r, null)).ToList();
    }

    private IQueryable<AccountRequestItem> OpenItemsOf(IReadOnlyList<int> departmentIds)
        => _db.AccountRequestItems.AsNoTracking()
            .Include(i => i.AccountType)
            .Include(i => i.AssignedDept)
            .Include(i => i.Request).ThenInclude(r => r!.Employee)
            .Where(i => departmentIds.Contains(i.AssignedDeptId)
                        && (i.Request!.Status == RequestStatus.Submitted || i.Request.Status == RequestStatus.InProgress));

    public async Task<IReadOnlyList<RequestItemDto>> GetMyPendingItemsAsync(CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        var items = await OpenItemsOf(actor.HandledDepartmentIds)
            .Where(i => i.Status != ItemStatus.Completed && i.Status != ItemStatus.Rejected)
            .OrderBy(i => i.RequestId)
            .ToListAsync(ct);
        return items.Select(i => MapItem(i, actor)).ToList();
    }

    public async Task<IReadOnlyList<RequestItemDto>> GetMyApprovalItemsAsync(CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        var items = await OpenItemsOf(actor.ApprovedDepartmentIds)
            .Where(i => i.ApprovedAt == null)
            .OrderBy(i => i.RequestId)
            .ToListAsync(ct);
        // Items waiting for a decision come first.
        return items.Select(i => MapItem(i, actor)).OrderByDescending(i => i.CanApprove).ToList();
    }

    // ------------------------------------------------------- department work

    public async Task<Result> UpdateItemAsync(UpdateItemDto dto, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        var submitted = false;
        var result = await ConcurrencyRetry.RunAsync(_db, async () =>
        {
            var item = await _db.AccountRequestItems
                .Include(i => i.Request)
                .FirstOrDefaultAsync(i => i.Id == dto.ItemId, ct);

            if (item is null) return Result.Fail("Request item not found.");
            if (!actor.Handles(item) && !actor.Approves(item))
                return Result.Fail("This item is not assigned to your department.");
            if (item.Request!.Status is not (RequestStatus.Submitted or RequestStatus.InProgress))
                return Result.Fail("This request is not open for processing.");
            if (item.Status is ItemStatus.Completed or ItemStatus.Rejected)
                return Result.Fail(item.ApprovedAt is null
                    ? "This item is already submitted for approval. The department approver can return it if it needs changes."
                    : "This item is already approved.");

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
            submitted = dto.Status is ItemStatus.Completed or ItemStatus.Rejected;

            await RecomputeAsync(item.Request, ct);
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync("UpdateItem", nameof(AccountRequestItem), item.Id.ToString(), $"{dto.Status}", ct);
            return Result.Success();
        });

        if (result.Succeeded && submitted) await NotifyApproversAsync(dto.ItemId, ct);
        return result;
    }

    public async Task<Result> ApproveItemAsync(int itemId, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        return await ConcurrencyRetry.RunAsync(_db, async () =>
        {
            var (item, error) = await LoadForDecisionAsync(itemId, actor, ct);
            if (item is null) return Result.Fail(error!);

            item.ApprovedBy = _currentUser.UserId;
            item.ApprovedByName = _currentUser.UserName;
            item.ApprovedAt = DateTime.UtcNow;
            item.ReturnReason = null;
            item.ReturnedAt = null;
            if (item.Status == ItemStatus.Completed)
                await ApplyToLedgerAsync(item, ct);

            await RecomputeAsync(item.Request!, ct);
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync("ApproveItem", nameof(AccountRequestItem), item.Id.ToString(), $"{item.Status}", ct);
            return Result.Success();
        });
    }

    public async Task<Result> ReturnItemAsync(int itemId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Result.Fail("Enter a reason for returning the item.");
        var actor = await GetActorAsync(ct);
        var result = await ConcurrencyRetry.RunAsync(_db, async () =>
        {
            var (item, error) = await LoadForDecisionAsync(itemId, actor, ct);
            if (item is null) return Result.Fail(error!);

            item.Status = ItemStatus.WIP;
            item.ReturnReason = reason.Trim();
            item.ReturnedAt = DateTime.UtcNow;

            await RecomputeAsync(item.Request!, ct);
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync("ReturnItem", nameof(AccountRequestItem), item.Id.ToString(), item.ReturnReason, ct);
            return Result.Success();
        });

        if (result.Succeeded) await NotifyReturnAsync(itemId, ct);
        return result;
    }

    /// <summary>A submitted, unapproved item of an open request, for one of its department's approvers.</summary>
    private async Task<(AccountRequestItem? Item, string? Error)> LoadForDecisionAsync(int itemId, Actor actor, CancellationToken ct)
    {
        var item = await _db.AccountRequestItems
            .Include(i => i.Request)
            .FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item?.Request is null || !actor.Approves(item)) return (null, "Request item not found.");
        if (item.Request.Status is not (RequestStatus.Submitted or RequestStatus.InProgress))
            return (null, "This request is not open for approval.");
        if (item.Status is not (ItemStatus.Completed or ItemStatus.Rejected))
            return (null, "The handlers have not completed or rejected this item yet.");
        if (item.ApprovedAt is not null) return (null, "This item is already approved.");
        return (item, null);
    }

    /// <summary>
    /// Recompute the master status from all items. The request row is always written so its row
    /// version catches a parallel update by another department; the retry then re-reads the items
    /// that department just changed.
    /// </summary>
    private async Task RecomputeAsync(AccountRequest request, CancellationToken ct)
    {
        var siblings = await _db.AccountRequestItems.Where(i => i.RequestId == request.Id).ToListAsync(ct);
        var newStatus = _workflow.EvaluateRequestStatus(siblings);
        request.Status = newStatus;
        request.ModifiedAt = DateTime.UtcNow;
        if (newStatus == RequestStatus.Completed && request.CompletedAt is null)
            request.CompletedAt = DateTime.UtcNow;
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

    private async Task<IReadOnlyList<string>> EmailsOfAsync(IEnumerable<int> departmentIds, AssignmentRole role, CancellationToken ct)
    {
        var assignees = await _assignments.GetAssigneesAsync(departmentIds, WorkArea.Account, role, ct);
        return assignees.Values.SelectMany(u => u)
            .Select(u => u.Email)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!)
            .Distinct()
            .ToList();
    }

    /// <summary>One consolidated email per department to its handlers, listing that department's account types.</summary>
    private async Task NotifyHandlersAsync(AccountRequest request, CancellationToken ct)
    {
        foreach (var group in request.Items.GroupBy(i => i.AssignedDeptId))
        {
            var emails = await EmailsOfAsync(new[] { group.Key }, AssignmentRole.Handler, ct);
            if (emails.Count == 0) continue;

            var typeList = string.Join(", ", group.Select(i => i.AccountType?.Name));
            var subject = $"[Action Required] Account provisioning for {request.Employee?.Name} ({request.RequestNo})";
            var body = $"""
                <p>Hello,</p>
                <p>Please log in to the HR System to process the following account type(s) for
                <b>{request.Employee?.Name}</b> (No. {request.Employee?.EmployeeNo}) under Account Checklist:</p>
                <p><b>{typeList}</b></p>
                <p>Request: {request.RequestNo} &middot; Type: {request.RequestType}</p>
                <p>Regards,<br/>HR System</p>
                """;
            _email.Enqueue(new EmailMessage(emails, subject, body));
        }
    }

    /// <summary>Tell the item's department approvers it is waiting for their approval.</summary>
    private async Task NotifyApproversAsync(int itemId, CancellationToken ct)
    {
        var item = await LoadForNoticeAsync(itemId, ct);
        if (item is null) return;
        var emails = await EmailsOfAsync(new[] { item.AssignedDeptId }, AssignmentRole.Approver, ct);
        if (emails.Count == 0) return;

        _email.Enqueue(new EmailMessage(
            emails,
            $"[Approval Required] {item.AccountType?.Name} for {item.Request?.Employee?.Name} ({item.Request?.RequestNo})",
            $"<p>{item.AccountType?.Name} for <b>{item.Request?.Employee?.Name}</b> ({item.Request?.Employee?.EmployeeNo}) was marked <b>{item.Status}</b>.</p>" +
            "<p>Please approve it, or return it to the handlers, in HR System under Account Approval.</p>"));
    }

    private async Task NotifyReturnAsync(int itemId, CancellationToken ct)
    {
        var item = await LoadForNoticeAsync(itemId, ct);
        if (item is null) return;
        var emails = await EmailsOfAsync(new[] { item.AssignedDeptId }, AssignmentRole.Handler, ct);
        if (emails.Count == 0) return;

        _email.Enqueue(new EmailMessage(
            emails,
            $"[Returned] {item.AccountType?.Name} for {item.Request?.Employee?.Name} ({item.Request?.RequestNo})",
            $"<p>The department approver returned {item.AccountType?.Name} for <b>{item.Request?.Employee?.Name}</b> ({item.Request?.Employee?.EmployeeNo}).</p>" +
            $"<p>Reason: {System.Net.WebUtility.HtmlEncode(item.ReturnReason)}</p><p>Please update it under Account Checklist.</p>"));
    }

    private Task<AccountRequestItem?> LoadForNoticeAsync(int itemId, CancellationToken ct)
        => _db.AccountRequestItems.AsNoTracking()
            .Include(i => i.AccountType)
            .Include(i => i.Request).ThenInclude(r => r!.Employee)
            .FirstOrDefaultAsync(i => i.Id == itemId, ct);

    private static RequestDto MapRequest(AccountRequest r, Actor? actor) => new()
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
        Items = r.Items.Select(i => MapItem(i, actor)).ToList(),
        Attachments = r.Attachments.Select(a => new AttachmentDto
        {
            Id = a.Id, FileName = a.FileName, FileSize = a.FileSize
        }).ToList()
    };

    /// <summary>With <paramref name="actor"/>, the DTO also says what the current user may do with the item.</summary>
    private static RequestItemDto MapItem(AccountRequestItem i, Actor? actor) => new()
    {
        Id = i.Id,
        RequestId = i.RequestId,
        RequestNo = i.Request?.RequestNo ?? string.Empty,
        EmployeeName = i.Request?.Employee?.Name ?? string.Empty,
        EmployeeNo = i.Request?.Employee?.EmployeeNo ?? string.Empty,
        RequestType = i.Request?.RequestType ?? default,
        RequestStatus = i.Request?.Status ?? default,
        AccountTypeId = i.AccountTypeId,
        AccountTypeName = i.AccountType?.Name ?? string.Empty,
        AssignedDeptId = i.AssignedDeptId,
        AssignedDeptName = i.AssignedDept?.Name ?? string.Empty,
        Status = i.Status,
        AccountValue = i.AccountValue,
        RequestDetail = i.RequestDetail,
        ResultRemark = i.ResultRemark,
        HandledAt = i.HandledAt,
        ApprovedByName = i.ApprovedByName,
        ApprovedAt = i.ApprovedAt,
        ReturnReason = i.ReturnReason,
        IsHandler = actor?.Handles(i) ?? false,
        IsApprover = actor?.Approves(i) ?? false
    };
}
