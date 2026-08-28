using HRSystem.Application.Common;
using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Domain.Entities;
using HRSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRSystem.Application.Services;

/// <summary>
/// Orchestrates the account provisioning workflow (Modules 3 &amp; 4):
/// create draft -> fan out into items -> submit -> dispatch to departments ->
/// department completes items -> ledger is updated -> master status recomputed.
/// </summary>
public class AccountRequestService : IAccountRequestService
{
    private readonly IAppDbContext _db;
    private readonly WorkflowService _workflow;
    private readonly IEmailService _email;
    private readonly IFileStorageService _files;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly ILogger<AccountRequestService> _logger;

    public AccountRequestService(
        IAppDbContext db,
        WorkflowService workflow,
        IEmailService email,
        IFileStorageService files,
        ICurrentUser currentUser,
        IAuditService audit,
        ILogger<AccountRequestService> logger)
    {
        _db = db;
        _workflow = workflow;
        _email = email;
        _files = files;
        _currentUser = currentUser;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<int>> CreateAsync(CreateRequestDto dto, CancellationToken ct = default)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == dto.EmployeeId && !e.IsDeleted, ct);
        if (employee is null) return Result<int>.Fail("Employee not found.");
        if (dto.AccountTypeIds.Count == 0) return Result<int>.Fail("Select at least one account type.");

        // Load the selected account types together with their responsible dept,
        // restricted to those visible on this employee's requisition form
        // (Staff or AE/Sales/SA).
        var audience = employee.Category == EmployeeCategory.AE
            ? AccountTypeAudience.AEOnly
            : AccountTypeAudience.StaffOnly;

        var accountTypes = await _db.AccountTypes
            .Where(a => dto.AccountTypeIds.Contains(a.Id) && a.IsActive
                        && (a.Audience == AccountTypeAudience.Both || a.Audience == audience))
            .ToListAsync(ct);

        if (accountTypes.Count == 0) return Result<int>.Fail("No valid account types selected.");

        var request = new AccountRequest
        {
            RequestNo = await GenerateRequestNoAsync(ct),
            EmployeeId = employee.Id,
            RequestType = dto.RequestType,
            Status = RequestStatus.Draft,
            Remark = dto.Remark,
            IsNewHeadcount = dto.IsNewHeadcount,
            ReplacementOf = dto.ReplacementOf,
            LastDay = dto.LastDay,
            CreatedBy = _currentUser.UserId
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
        await _audit.LogAsync("CreateRequest", nameof(AccountRequest), request.Id.ToString(), request.RequestNo, ct);
        return Result<int>.Success(request.Id);
    }

    public async Task<Result> SubmitAsync(int requestId, CancellationToken ct = default)
    {
        var request = await _db.AccountRequests
            .Include(r => r.Employee)
            .Include(r => r.Items).ThenInclude(i => i.AccountType)
            .Include(r => r.Items).ThenInclude(i => i.AssignedDept)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);

        if (request is null) return Result.Fail("Request not found.");
        if (request.Status != RequestStatus.Draft) return Result.Fail("Only draft requests can be submitted.");
        if (request.Items.Count == 0) return Result.Fail("Request has no items.");

        // Snapshot assigned department heads at submit time.
        foreach (var item in request.Items)
            item.AssignedUserId = item.AssignedDept?.HeadUserId;

        request.Status = RequestStatus.Submitted;
        request.AppliedBy = _currentUser.UserId;
        request.AppliedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        // --- Notifications (queued, non-blocking) ---
        NotifyNewEmployee(request);
        NotifyDepartmentHeads(request);

        await _audit.LogAsync("SubmitRequest", nameof(AccountRequest), request.Id.ToString(), request.RequestNo, ct);
        _logger.LogInformation("Request {RequestNo} submitted with {Count} items.", request.RequestNo, request.Items.Count);
        return Result.Success();
    }

    public async Task<RequestDto?> GetAsync(int requestId, CancellationToken ct = default)
    {
        var r = await _db.AccountRequests.AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Items).ThenInclude(i => i.AccountType)
            .Include(x => x.Items).ThenInclude(i => i.AssignedDept)
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == requestId, ct);

        return r is null ? null : MapRequest(r);
    }

    public async Task<IReadOnlyList<RequestDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _db.AccountRequests.AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Items).ThenInclude(i => i.AccountType)
            .Include(x => x.Items).ThenInclude(i => i.AssignedDept)
            .OrderByDescending(x => x.Id)
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
                        && i.Request!.Status != RequestStatus.Draft)
            .OrderBy(i => i.RequestId)
            .ToListAsync(ct);

        return items.Select(MapItem).ToList();
    }

    public async Task<Result> UpdateItemAsync(UpdateItemDto dto, CancellationToken ct = default)
    {
        var item = await _db.AccountRequestItems
            .Include(i => i.Request)
            .FirstOrDefaultAsync(i => i.Id == dto.ItemId, ct);

        if (item is null) return Result.Fail("Request item not found.");
        if (item.Request!.Status == RequestStatus.Draft) return Result.Fail("Request is not submitted yet.");

        // Validate the transition through the single state-machine authority.
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

        // On completion, reflect the change into the standing account ledger.
        if (dto.Status == ItemStatus.Completed)
            await ApplyToLedgerAsync(item, ct);

        // Recompute the master status from all sibling items.
        var siblings = await _db.AccountRequestItems
            .Where(i => i.RequestId == item.RequestId)
            .ToListAsync(ct);

        var newStatus = _workflow.EvaluateRequestStatus(siblings);
        item.Request.Status = newStatus;
        if (newStatus == RequestStatus.Completed && item.Request.CompletedAt is null)
            item.Request.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("UpdateItem", nameof(AccountRequestItem), item.Id.ToString(),
            $"{dto.Status}", ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<AccountTypeOptionDto>> GetAccountTypeOptionsAsync(int employeeId, CancellationToken ct = default)
    {
        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId && !e.IsDeleted, ct);
        if (employee is null) return Array.Empty<AccountTypeOptionDto>();

        var audience = employee.Category == EmployeeCategory.AE
            ? AccountTypeAudience.AEOnly
            : AccountTypeAudience.StaffOnly;

        var options = await _db.AccountTypes.AsNoTracking()
            .Include(a => a.ResponsibleDept)
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

        return options;
    }

    public async Task<Result> AddAttachmentAsync(
        int requestId, Stream content, string fileName, string? contentType, CancellationToken ct = default)
    {
        var request = await _db.AccountRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return Result.Fail("Request not found.");

        var stored = await _files.SaveAsync(content, fileName, contentType, ct);
        _db.Attachments.Add(new Attachment
        {
            RequestId = requestId,
            FileName = stored.FileName,
            FilePath = stored.RelativePath,
            ContentType = stored.ContentType,
            FileSize = stored.Size,
            CreatedBy = _currentUser.UserId
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

    /// <summary>Send a welcome / notification email to the target employee.</summary>
    private void NotifyNewEmployee(AccountRequest request)
    {
        var to = request.Employee?.Email;
        if (string.IsNullOrWhiteSpace(to)) return;

        var subject = request.RequestType == RequestType.Onboard
            ? $"Welcome aboard - account setup in progress ({request.RequestNo})"
            : $"Your account request has been submitted ({request.RequestNo})";

        var body = $"""
            <p>Dear {request.Employee?.Name},</p>
            <p>Your account request <b>{request.RequestNo}</b> has been submitted and is being processed
            by the relevant departments. You will be notified once it is completed.</p>
            <p>Regards,<br/>HR System</p>
            """;

        _email.Enqueue(new EmailMessage(new[] { to }, subject, body));
    }

    /// <summary>
    /// Notify each responsible department head, grouped so a head who owns
    /// several account types receives a single consolidated email.
    /// </summary>
    private void NotifyDepartmentHeads(AccountRequest request)
    {
        var groups = request.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.AssignedUserId))
            .GroupBy(i => i.AssignedUserId!);

        foreach (var group in groups)
        {
            var deptHeadEmail = group.First().AssignedDept?.HeadUserId; // resolved to email in infra
            if (string.IsNullOrWhiteSpace(deptHeadEmail)) continue;

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

            _email.Enqueue(new EmailMessage(new[] { deptHeadEmail }, subject, body));
        }
    }

    /// <summary>Generate a sequential, human-friendly request number.</summary>
    private async Task<string> GenerateRequestNoAsync(CancellationToken ct)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"REQ-{year}-";
        var count = await _db.AccountRequests.CountAsync(r => r.RequestNo.StartsWith(prefix), ct);
        return $"{prefix}{(count + 1):D5}";
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
        Items = r.Items.Select(MapItem).ToList(),
        Attachments = r.Attachments.Select(a => new AttachmentDto
        {
            Id = a.Id, FileName = a.FileName, FileSize = a.FileSize
        }).ToList()
    };

    private static RequestItemDto MapItem(AccountRequestItem i) => new()
    {
        Id = i.Id,
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
