using HRSystem.Application.Common;
using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using HRSystem.Domain.Entities;
using HRSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Services;

/// <summary>
/// Onboarding and departure workflow. Onboarding: draft -> submit (its account request is dispatched)
/// -> HR finalizes once the accounts are open. Departure: draft -> submit (one task per active template
/// department) -> each department's handlers complete their checklist and its approvers approve it, in
/// parallel -> the head of the leaver's department approves, or returns chosen departments' tasks ->
/// HR / management (departures.approve) approves, completing it, or returns it to the head. The head
/// step is skipped when the leaver is that head. Every step is written to the request's log.
/// </summary>
public class ChecklistService : IChecklistService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IEmailService _email;
    private readonly IUserDirectoryService _users;
    private readonly IAccountRequestService _accountRequests;
    private readonly IAssignmentService _assignments;

    public ChecklistService(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditService audit,
        IEmailService email,
        IUserDirectoryService users,
        IAccountRequestService accountRequests,
        IAssignmentService assignments)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _email = email;
        _users = users;
        _accountRequests = accountRequests;
        _assignments = assignments;
    }

    public static string ManagePermission(ChecklistKind kind)
        => kind == ChecklistKind.Onboarding ? Permissions.OnboardingManage : Permissions.DeparturesManage;

    private bool CanManage(ChecklistKind kind) => _currentUser.HasPermission(ManagePermission(kind));

    private static string Label(ChecklistKind kind) => kind == ChecklistKind.Onboarding ? "onboarding" : "departure";

    private static IQueryable<ChecklistRequest> Open(IQueryable<ChecklistRequest> q)
        => q.Where(r => r.Status != ChecklistStatus.Completed && r.Status != ChecklistStatus.Cancelled);

    // ------------------------------------------------------------------ read

    public async Task<IReadOnlyList<ChecklistRequestDto>> GetListAsync(ChecklistKind kind, CancellationToken ct = default)
    {
        if (!CanManage(kind)) return Array.Empty<ChecklistRequestDto>();

        var requests = await _db.ChecklistRequests.AsNoTracking()
            .Include(r => r.Employee)
            .Include(r => r.AccountRequest)
            .Include(r => r.Tasks).ThenInclude(t => t.Items)
            .Where(r => r.Kind == kind)
            .OrderByDescending(r => r.Id)
            .ToListAsync(ct);

        return requests.Select(MapRequest).ToList();
    }

    public async Task<int?> GetOpenRequestIdAsync(ChecklistKind kind, int employeeId, CancellationToken ct = default)
    {
        return await Open(_db.ChecklistRequests.AsNoTracking())
            .Where(r => r.Kind == kind && r.EmployeeId == employeeId)
            .OrderByDescending(r => r.Id)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<EmployeeOptionDto>> GetEligibleEmployeesAsync(ChecklistKind kind, CancellationToken ct = default)
    {
        if (!CanManage(kind)) return Array.Empty<EmployeeOptionDto>();

        var openEmployeeIds = Open(_db.ChecklistRequests).Where(r => r.Kind == kind).Select(r => r.EmployeeId);
        return await _db.Employees.AsNoTracking()
            .Where(e => e.Status == EmployeeStatus.Active && !openEmployeeIds.Contains(e.Id))
            .OrderBy(e => e.Name)
            .Select(e => new EmployeeOptionDto(e.Id, e.Name + " (" + e.EmployeeNo + ")"))
            .ToListAsync(ct);
    }

    public async Task<ChecklistRequestDto?> GetAsync(int requestId, CancellationToken ct = default)
    {
        var request = await _db.ChecklistRequests.AsNoTracking()
            .Include(r => r.Employee).ThenInclude(e => e!.Department)
            .Include(r => r.AccountRequest)
            .Include(r => r.Tasks.OrderBy(t => t.SortOrder))
            .ThenInclude(t => t.Items.OrderBy(i => i.SortOrder))
            .Include(r => r.Logs)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null || !CanManage(request.Kind)) return null;

        var dto = MapRequest(request);
        dto.CanManagementDecide = CanManagementDecide(request);
        if (request.SubmittedBy is not null)
            dto.SubmittedByName = request.Logs.FirstOrDefault(l => l.Action == ChecklistLogAction.Submitted)?.UserName
                ?? (await _users.GetByIdAsync(request.SubmittedBy, ct))?.UserName;
        dto.ActiveAccounts = await _db.EmployeeAccounts.AsNoTracking()
            .Where(a => a.EmployeeId == request.EmployeeId && a.Status == AccountStatus.Active)
            .OrderBy(a => a.AccountType!.SortOrder).ThenBy(a => a.AccountType!.Name)
            .Select(a => new ChecklistHeldAccountDto(a.AccountType!.Name, a.AccountValue, a.AccountType.ResponsibleDept!.Name, a.OpenedAt))
            .ToListAsync(ct);
        dto.EmployeeAttachments = await _db.EmployeeAttachments.AsNoTracking()
            .Where(a => a.EmployeeId == request.EmployeeId)
            .OrderBy(a => a.Id)
            .Select(a => new EmployeeAttachmentDto { Id = a.Id, FileName = a.FileName, ContentType = a.ContentType, FileSize = a.FileSize })
            .ToListAsync(ct);
        return dto;
    }

    // ---------------------------------------------------------------- create

    public async Task<Result<ChecklistCreatePageDto>> GetCreatePageAsync(ChecklistKind kind, int employeeId, CancellationToken ct = default)
    {
        if (!CanManage(kind)) return Result<ChecklistCreatePageDto>.Fail($"You do not have permission to start {Label(kind)}.");

        var employee = await _db.Employees.AsNoTracking()
            .Include(e => e.Department)
            .FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        var error = await CheckEligibleAsync(kind, employee, ct);
        if (error is not null) return Result<ChecklistCreatePageDto>.Fail(error);

        var options = (await _accountRequests.GetAccountTypeOptionsAsync(employee!.Id, ct)).ToList();
        var activeAccounts = new Dictionary<int, string?>();
        if (kind == ChecklistKind.Departure)
        {
            var held = await _db.EmployeeAccounts.AsNoTracking()
                .Where(a => a.EmployeeId == employee.Id && a.Status == AccountStatus.Active)
                .Select(a => new
                {
                    a.AccountTypeId,
                    a.AccountValue,
                    Option = new AccountTypeOptionDto
                    {
                        Id = a.AccountTypeId,
                        Name = a.AccountType!.Name,
                        DeptId = a.AccountType.ResponsibleDeptId,
                        DeptName = a.AccountType.ResponsibleDept!.Name,
                        SortOrder = a.AccountType.SortOrder
                    }
                })
                .ToListAsync(ct);
            foreach (var account in held)
            {
                activeAccounts[account.AccountTypeId] = account.AccountValue;
                // Accounts outside the employee's current form (category changed, type retired) must still be disabled.
                if (options.All(o => o.Id != account.AccountTypeId)) options.Add(account.Option);
            }
        }

        var page = new ChecklistCreatePageDto
        {
            Kind = kind,
            EmployeeId = employee.Id,
            EmployeeNo = employee.EmployeeNo,
            EmployeeName = employee.Name,
            Department = employee.Department?.Name,
            Position = employee.Position,
            JoinDate = employee.JoinDate,
            Templates = await GetTemplatePreviewAsync(kind, ct),
            AccountTypeOptions = options,
            ActiveAccounts = activeAccounts
        };
        if (kind == ChecklistKind.Departure)
        {
            var finalApprover = await ResolveFinalApproverAsync(employee.Id, ct);
            page.FinalApproverName = finalApprover.User?.Display;
            page.FinalApprovalError = finalApprover.Error;
        }
        return Result<ChecklistCreatePageDto>.Success(page);
    }

    /// <summary>
    /// The head of the leaver's department, who gives a departure its final approval. User is null
    /// without an error when the leaver is that head: HR then finalizes.
    /// </summary>
    private async Task<(UserDirectoryEntry? User, string? Error)> ResolveFinalApproverAsync(int employeeId, CancellationToken ct)
    {
        var leaver = await _db.Employees.AsNoTracking()
            .Include(e => e.Department).ThenInclude(d => d!.Manager)
            .FirstAsync(e => e.Id == employeeId, ct);
        if (leaver.Department is null)
            return (null, "The employee has no department, so there is no department head for the final approval. Set the employee's department first.");

        var head = leaver.Department.Manager;
        if (head?.UserId is null)
            return (null, $"Department {leaver.Department.Name} has no manager with a login for the final approval. Set one under Departments first.");
        if (head.Id == leaver.Id) return (null, null);

        var user = await _users.GetByIdAsync(head.UserId, ct);
        return user is null
            ? (null, $"The manager login of {leaver.Department.Name} no longer exists. Set the manager again under Departments.")
            : (user, null);
    }

    public async Task<Result<int>> CreateDraftAsync(CreateChecklistRequestDto dto, CancellationToken ct = default)
    {
        if (!CanManage(dto.Kind)) return Result<int>.Fail($"You do not have permission to start {Label(dto.Kind)}.");

        if (dto.Kind == ChecklistKind.Departure)
        {
            if (dto.LastWorkingDate is null || dto.LastEmploymentDate is null)
                return Result<int>.Fail("Last working date and last employment date are required.");
            if (dto.LastEmploymentDate < dto.LastWorkingDate)
                return Result<int>.Fail("Last employment date must be on or after last working date.");
        }
        else if (dto.StartDate is null)
        {
            return Result<int>.Fail("Start date is required.");
        }

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == dto.EmployeeId, ct);
        var error = await CheckEligibleAsync(dto.Kind, employee, ct);
        if (error is not null) return Result<int>.Fail(error);

        var request = new ChecklistRequest
        {
            Kind = dto.Kind,
            RequestNo = $"TMP-{Guid.NewGuid():N}"[..16],
            EmployeeId = dto.EmployeeId,
            Status = ChecklistStatus.Draft,
            StartDate = dto.Kind == ChecklistKind.Onboarding ? dto.StartDate : null,
            LastWorkingDate = dto.Kind == ChecklistKind.Departure ? dto.LastWorkingDate : null,
            LastEmploymentDate = dto.Kind == ChecklistKind.Departure ? dto.LastEmploymentDate : null,
            Reason = dto.Kind == ChecklistKind.Departure ? dto.Reason : null,
            Remark = dto.Remark,
            CreatedBy = _currentUser.UserId
        };
        _db.ChecklistRequests.Add(request);
        await _db.SaveChangesAsync(ct);
        request.RequestNo = $"{(dto.Kind == ChecklistKind.Onboarding ? "ONB" : "DEP")}-{DateTime.UtcNow.Year}-{request.Id:D5}";
        await _db.SaveChangesAsync(ct);

        // The accounts to open (onboarding) or disable (departure) travel as a linked account request.
        if (dto.AccountTypeIds.Count > 0)
        {
            var accountRequest = await _accountRequests.CreateAsync(new CreateRequestDto
            {
                EmployeeId = dto.EmployeeId,
                RequestType = dto.Kind == ChecklistKind.Onboarding ? RequestType.Onboard : RequestType.Offboard,
                AccountTypeIds = dto.AccountTypeIds,
                Details = dto.Kind == ChecklistKind.Onboarding ? dto.AccountDetails : new Dictionary<int, string>(),
                IsNewHeadcount = dto.Kind == ChecklistKind.Onboarding ? true : null,
                Remark = $"Raised with {Label(dto.Kind)} {request.RequestNo}."
            }, ct);
            if (!accountRequest.Succeeded)
            {
                _db.ChecklistRequests.Remove(request);
                await _db.SaveChangesAsync(ct);
                return Result<int>.Fail(accountRequest.Error!);
            }

            request.AccountRequestId = accountRequest.Value;
            await _db.SaveChangesAsync(ct);
        }

        await _audit.LogAsync($"Create{dto.Kind}Draft", nameof(ChecklistRequest), request.Id.ToString(), request.RequestNo, ct);
        return Result<int>.Success(request.Id);
    }

    private async Task<string?> CheckEligibleAsync(ChecklistKind kind, Employee? employee, CancellationToken ct)
    {
        if (employee is null || employee.Status != EmployeeStatus.Active)
            return $"Only active employees can start {Label(kind)}.";
        var hasOpen = await Open(_db.ChecklistRequests).AnyAsync(r => r.Kind == kind && r.EmployeeId == employee.Id, ct);
        return hasOpen ? $"This employee already has an open {Label(kind)} request." : null;
    }

    private async Task<IReadOnlyList<ChecklistTemplatePreviewDto>> GetTemplatePreviewAsync(ChecklistKind kind, CancellationToken ct)
    {
        // Onboarding only opens accounts; its old templates are no longer dispatched.
        if (kind == ChecklistKind.Onboarding) return Array.Empty<ChecklistTemplatePreviewDto>();

        var templates = await _db.ChecklistTemplates.AsNoTracking()
            .Where(t => t.Kind == kind && t.IsActive && !t.IsDeleted)
            .OrderBy(t => t.SortOrder)
            .Select(t => new
            {
                t.DepartmentId,
                Dto = new ChecklistTemplatePreviewDto
                {
                    DepartmentName = t.Department!.Name,
                    ItemCount = t.Items.Count(i => !i.IsDeleted)
                }
            })
            .ToListAsync(ct);

        var departmentIds = templates.Select(t => t.DepartmentId).ToList();
        var handlers = await _assignments.GetAssigneesAsync(departmentIds, WorkArea.Departure, AssignmentRole.Handler, ct);
        var approvers = await _assignments.GetAssigneesAsync(departmentIds, WorkArea.Departure, AssignmentRole.Approver, ct);
        foreach (var t in templates)
        {
            t.Dto.HandlerNames = AssignmentService.Names(handlers[t.DepartmentId]);
            t.Dto.ApproverNames = AssignmentService.Names(approvers[t.DepartmentId]);
        }
        return templates.Select(t => t.Dto).ToList();
    }

    // -------------------------------------------------------- submit/finalize

    public async Task<Result> SubmitAsync(int requestId, CancellationToken ct = default)
    {
        var snapshot = await _db.ChecklistRequests.AsNoTracking()
            .Include(r => r.AccountRequest)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (snapshot is null || !CanManage(snapshot.Kind)) return Result.Fail("Request not found.");
        if (snapshot.Status != ChecklistStatus.Draft) return Result.Fail("Only draft requests can be submitted.");

        var isDeparture = snapshot.Kind == ChecklistKind.Departure;
        var templates = !isDeparture
            ? new List<ChecklistTemplate>()
            : await _db.ChecklistTemplates.AsNoTracking()
                .Include(t => t.Items)
                .Include(t => t.Department)
                .Where(t => t.Kind == snapshot.Kind && t.IsActive && !t.IsDeleted)
                .OrderBy(t => t.SortOrder)
                .ToListAsync(ct);
        UserDirectoryEntry? finalApprover = null;
        if (isDeparture)
        {
            if (templates.Count == 0) return Result.Fail("No active departure checklist templates found.");

            // Check every department can receive and approve its task before anything is dispatched.
            var coverageError = await _assignments.CheckCoverageAsync(templates.Select(t => t.DepartmentId), WorkArea.Departure, ct);
            if (coverageError is not null) return Result.Fail(coverageError);

            var resolved = await ResolveFinalApproverAsync(snapshot.EmployeeId, ct);
            if (resolved.Error is not null) return Result.Fail(resolved.Error);
            finalApprover = resolved.User;
        }

        // Submit the linked account request first; if its routing fails nothing is dispatched.
        if (snapshot.AccountRequest is { Status: RequestStatus.Draft })
        {
            var submitted = await _accountRequests.SubmitAsync(snapshot.AccountRequest.Id, ct);
            if (!submitted.Succeeded) return Result.Fail($"Account request {snapshot.AccountRequest.RequestNo}: {submitted.Error}");
        }

        var request = await _db.ChecklistRequests.Include(r => r.Employee).FirstAsync(r => r.Id == requestId, ct);
        var tasks = templates.Select(template =>
        {
            var task = new ChecklistTask
            {
                ChecklistRequestId = request.Id,
                AssignedDepartmentId = template.DepartmentId,
                DepartmentName = template.Department!.Name,
                Status = ChecklistTaskStatus.NotStarted,
                IsRequired = template.IsRequired,
                SortOrder = template.SortOrder,
                CreatedBy = _currentUser.UserId
            };
            foreach (var item in template.Items.Where(i => !i.IsDeleted).OrderBy(i => i.SortOrder))
            {
                task.Items.Add(new ChecklistTaskItem
                {
                    Description = item.Description,
                    SortOrder = item.SortOrder,
                    IsRequired = item.IsRequired,
                    AllowAttachment = request.Kind == ChecklistKind.Departure && item.AllowAttachment,
                    CreatedBy = _currentUser.UserId
                });
            }
            return task;
        }).ToList();

        _db.ChecklistTasks.AddRange(tasks);
        request.Status = ChecklistStatus.Submitted;
        request.SubmittedAt = DateTime.UtcNow;
        request.SubmittedBy = _currentUser.UserId;
        request.FinalApproverUserId = finalApprover?.UserId;
        request.FinalApproverUserName = finalApprover?.UserName;
        AddLog(request.Id, ChecklistLogAction.Submitted, null);
        await _db.SaveChangesAsync(ct);

        await NotifyHandlersAsync(request, tasks, ct);
        await _audit.LogAsync($"Submit{request.Kind}", nameof(ChecklistRequest), request.Id.ToString(), request.RequestNo, ct);
        return Result.Success();
    }

    /// <summary>Records a workflow step in the request's log; it is saved together with the change it describes.</summary>
    private void AddLog(int requestId, ChecklistLogAction action, string? remark, string? departments = null)
        => _db.ChecklistRequestLogs.Add(new ChecklistRequestLog
        {
            ChecklistRequestId = requestId,
            Action = action,
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim(),
            Departments = departments,
            UserId = _currentUser.UserId,
            UserName = _currentUser.UserName,
            At = DateTime.UtcNow
        });

    public Task<Result> FinalizeAsync(int requestId, CancellationToken ct = default)
        => ConcurrencyRetry.RunAsync(_db, () => FinalizeOnceAsync(requestId, ct));

    private async Task<Result> FinalizeOnceAsync(int requestId, CancellationToken ct)
    {
        var request = await _db.ChecklistRequests
            .Include(r => r.AccountRequest)
            .Include(r => r.Tasks)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null || !CanManage(request.Kind)) return Result.Fail("Request not found.");
        if (request.Kind == ChecklistKind.Departure)
            return Result.Fail("A departure is approved by the employee's department head and then HR / management under Departure Approval.");
        if (request.Status == ChecklistStatus.Completed) return Result.Fail("This request is already finalized.");
        if (request.Status == ChecklistStatus.Cancelled) return Result.Fail("This request was cancelled.");
        if (request.Status == ChecklistStatus.Draft) return Result.Fail("Submit the request first.");

        var error = CheckReadyToComplete(request);
        if (error is not null) return Result.Fail(error);

        request.Status = ChecklistStatus.Completed;
        request.FinalizedAt = DateTime.UtcNow;
        request.FinalizedBy = _currentUser.UserId;
        request.CompletedAt = request.FinalizedAt;
        AddLog(request.Id, ChecklistLogAction.Finalized, null);

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync($"Finalize{request.Kind}", nameof(ChecklistRequest), request.Id.ToString(), request.RequestNo, ct);
        return Result.Success();
    }

    /// <summary>Every required task completed and approved, and the linked account request done.</summary>
    private static string? CheckReadyToComplete(ChecklistRequest request)
    {
        var requiredTasks = request.Tasks.Where(t => t.IsRequired).ToList();
        if (request.Kind == ChecklistKind.Departure && requiredTasks.Count == 0)
            return "This request has no required department tasks.";
        if (requiredTasks.Any(t => t.Status is not (ChecklistTaskStatus.Completed or ChecklistTaskStatus.NotApplicable)))
            return "All required department tasks must be completed first.";
        if (requiredTasks.Any(t => t.ApprovedAt is null))
            return "All required department tasks must be approved by their department approvers first.";
        if (request.AccountRequest is not null && request.AccountRequest.Status != RequestStatus.Completed)
            return $"Account request {request.AccountRequest.RequestNo} is not completed yet.";
        return null;
    }

    private static bool HasFinalApprover(ChecklistRequest request)
        => request.Kind == ChecklistKind.Departure && request.FinalApproverUserId is not null;

    /// <summary>The current user is the head of the leaver's department, who approves after the departments.</summary>
    private bool IsFinalApprover(ChecklistRequest request)
        => HasFinalApprover(request) && !string.IsNullOrWhiteSpace(_currentUser.UserId) && request.FinalApproverUserId == _currentUser.UserId;

    /// <summary>The current user gives departures the last approval (HR / management).</summary>
    private bool IsManagementApprover(ChecklistRequest request)
        => request.Kind == ChecklistKind.Departure && _currentUser.HasPermission(Permissions.DeparturesApprove);

    /// <summary>Nobody decides on their own departure.</summary>
    private bool IsLeaver(ChecklistRequest request)
        => request.Employee?.UserId is { } leaverUserId && leaverUserId == _currentUser.UserId;

    private bool CanManagementDecide(ChecklistRequest request)
        => request.Status == ChecklistStatus.PendingManagementApproval && IsManagementApprover(request) && !IsLeaver(request);

    // ------------------------------------------------- head and HR / management approval

    private IQueryable<ChecklistRequest> Departures()
        => _db.ChecklistRequests.AsNoTracking()
            .Include(r => r.Employee)
            .Include(r => r.AccountRequest)
            .Include(r => r.Tasks)
            .Where(r => r.Kind == ChecklistKind.Departure);

    public async Task<IReadOnlyList<ChecklistRequestDto>> GetMyFinalReviewsAsync(CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId)) return Array.Empty<ChecklistRequestDto>();

        var requests = await Departures()
            .Where(r => r.FinalApproverUserId == userId && r.Status == ChecklistStatus.PendingFinalReview)
            .OrderBy(r => r.SubmittedAt)
            .ToListAsync(ct);
        return requests.Select(MapRequest).ToList();
    }

    public async Task<IReadOnlyList<ChecklistRequestDto>> GetMyManagementApprovalsAsync(CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId) || !_currentUser.HasPermission(Permissions.DeparturesApprove))
            return Array.Empty<ChecklistRequestDto>();

        var requests = await Departures()
            .Where(r => r.Status == ChecklistStatus.PendingManagementApproval && (r.Employee!.UserId == null || r.Employee.UserId != userId))
            .OrderBy(r => r.SubmittedAt)
            .ToListAsync(ct);
        return requests.Select(MapRequest).ToList();
    }

    public async Task<IReadOnlyList<ChecklistRequestDto>> GetMyFinalReviewHistoryAsync(ChecklistTaskHistoryQuery query, CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId)) return Array.Empty<ChecklistRequestDto>();
        if (query.Kind == ChecklistKind.Onboarding) return Array.Empty<ChecklistRequestDto>();

        // Departures the user heads, or decided on as HR / management, that are not waiting for them now.
        var isManagement = _currentUser.HasPermission(Permissions.DeparturesApprove);
        var q = Departures()
            .Where(r => r.FinalApproverUserId == userId
                        || r.Logs.Any(l => l.UserId == userId
                                           && (l.Action == ChecklistLogAction.ManagementApproved || l.Action == ChecklistLogAction.ManagementReturned)))
            .Where(r => !(r.Status == ChecklistStatus.PendingFinalReview && r.FinalApproverUserId == userId)
                        && !(isManagement && r.Status == ChecklistStatus.PendingManagementApproval));
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var k = query.Keyword.Trim();
            q = q.Where(r => r.RequestNo.Contains(k) || r.Employee!.Name.Contains(k) || r.Employee.EmployeeNo.Contains(k));
        }
        if (query.From is { } from) q = q.Where(r => r.SubmittedAt >= from.Date);
        if (query.To is { } to) q = q.Where(r => r.SubmittedAt < to.Date.AddDays(1));

        var requests = await q.OrderByDescending(r => r.SubmittedAt).Take(500).ToListAsync(ct);
        return requests.Select(MapRequest).ToList();
    }

    public async Task<ChecklistRequestDto?> GetFinalReviewAsync(int requestId, CancellationToken ct = default)
    {
        var request = await _db.ChecklistRequests.AsNoTracking()
            .Include(r => r.Employee)
            .Include(r => r.AccountRequest)
            .Include(r => r.Tasks.OrderBy(t => t.SortOrder))
            .ThenInclude(t => t.Items.OrderBy(i => i.SortOrder))
            .Include(r => r.Logs)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null || request.Status == ChecklistStatus.Draft) return null;
        if (!IsFinalApprover(request) && !IsManagementApprover(request)) return null;

        var dto = MapRequest(request);
        dto.CanHeadDecide = request.Status == ChecklistStatus.PendingFinalReview && IsFinalApprover(request);
        dto.CanManagementDecide = CanManagementDecide(request);
        return dto;
    }

    public Task<Result> ApproveByHeadAsync(int requestId, string remark, CancellationToken ct = default)
        => ConcurrencyRetry.RunAsync(_db, () => ApproveByHeadOnceAsync(requestId, remark, ct));

    private async Task<Result> ApproveByHeadOnceAsync(int requestId, string remark, CancellationToken ct)
    {
        var request = await _db.ChecklistRequests
            .Include(r => r.Employee)
            .Include(r => r.Tasks)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null || !IsFinalApprover(request)) return Result.Fail("Request not found.");
        if (request.Status != ChecklistStatus.PendingFinalReview) return Result.Fail("This departure is not waiting for your approval.");
        if (string.IsNullOrWhiteSpace(remark)) return Result.Fail("Enter a remark for your approval.");
        // The account request is only checked at the last step, so it can finish while HR / management review.
        if (request.Tasks.Where(t => t.IsRequired).Any(t => t.ApprovedAt is null))
            return Result.Fail("All required department tasks must be approved by their department approvers first.");

        var now = DateTime.UtcNow;
        request.Status = ChecklistStatus.PendingManagementApproval;
        request.ModifiedAt = now;
        AddLog(request.Id, ChecklistLogAction.HeadApproved, remark);
        await _db.SaveChangesAsync(ct);

        await NotifyManagementApproversAsync(request, ct);
        await _audit.LogAsync("ApproveDepartureAsHead", nameof(ChecklistRequest), request.Id.ToString(), $"{request.RequestNo}: {remark.Trim()}", ct);
        return Result.Success();
    }

    public Task<Result> ApproveByManagementAsync(int requestId, string remark, CancellationToken ct = default)
        => ConcurrencyRetry.RunAsync(_db, () => ApproveByManagementOnceAsync(requestId, remark, ct));

    private async Task<Result> ApproveByManagementOnceAsync(int requestId, string remark, CancellationToken ct)
    {
        var request = await _db.ChecklistRequests
            .Include(r => r.Employee)
            .Include(r => r.AccountRequest)
            .Include(r => r.Tasks)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null || !IsManagementApprover(request)) return Result.Fail("Request not found.");
        if (IsLeaver(request)) return Result.Fail("You cannot approve your own departure.");
        if (request.Status != ChecklistStatus.PendingManagementApproval)
            return Result.Fail("This departure is not waiting for HR / management approval.");
        if (string.IsNullOrWhiteSpace(remark)) return Result.Fail("Enter a remark for your approval.");
        var error = CheckReadyToComplete(request);
        if (error is not null) return Result.Fail(error);

        var now = DateTime.UtcNow;
        request.Status = ChecklistStatus.Completed;
        request.FinalizedAt = now;
        request.FinalizedBy = _currentUser.UserId;
        request.CompletedAt = now;
        request.ModifiedAt = now;
        if (request.Employee is not null)
        {
            request.Employee.Status = EmployeeStatus.Resigned;
            request.Employee.ResignDate = request.LastEmploymentDate;
            request.Employee.ModifiedBy = _currentUser.UserId;
            request.Employee.ModifiedAt = now;
        }
        AddLog(request.Id, ChecklistLogAction.ManagementApproved, remark);
        await _db.SaveChangesAsync(ct);

        await NotifyCompletionAsync(request, remark, ct);
        await _audit.LogAsync("FinalizeDeparture", nameof(ChecklistRequest), request.Id.ToString(), $"{request.RequestNo}: {remark.Trim()}", ct);
        return Result.Success();
    }

    public Task<Result> ReturnToHeadAsync(int requestId, string reason, CancellationToken ct = default)
        => ConcurrencyRetry.RunAsync(_db, () => ReturnToHeadOnceAsync(requestId, reason, ct));

    private async Task<Result> ReturnToHeadOnceAsync(int requestId, string reason, CancellationToken ct)
    {
        var request = await _db.ChecklistRequests
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null || !IsManagementApprover(request)) return Result.Fail("Request not found.");
        if (IsLeaver(request)) return Result.Fail("You cannot decide on your own departure.");
        if (request.Status != ChecklistStatus.PendingManagementApproval)
            return Result.Fail("This departure is not waiting for HR / management approval.");
        if (!HasFinalApprover(request))
            return Result.Fail("The employee heads their own department, so choose the departments that must redo their tasks instead.");
        if (string.IsNullOrWhiteSpace(reason)) return Result.Fail("Enter a reason for returning the departure.");

        request.Status = ChecklistStatus.PendingFinalReview;
        request.ModifiedAt = DateTime.UtcNow;
        AddLog(request.Id, ChecklistLogAction.ManagementReturned, reason, request.FinalApproverUserName);
        await _db.SaveChangesAsync(ct);

        await NotifyHeadOfReturnAsync(request, reason, ct);
        await _audit.LogAsync("ReturnDepartureToHead", nameof(ChecklistRequest), request.Id.ToString(), $"{request.RequestNo}: {reason.Trim()}", ct);
        return Result.Success();
    }

    public Task<Result> ReturnTasksAsync(int requestId, IReadOnlyCollection<int> taskIds, string reason, CancellationToken ct = default)
        => ConcurrencyRetry.RunAsync(_db, () => ReturnTasksOnceAsync(requestId, taskIds, reason, ct));

    private async Task<Result> ReturnTasksOnceAsync(int requestId, IReadOnlyCollection<int> taskIds, string reason, CancellationToken ct)
    {
        var request = await _db.ChecklistRequests
            .Include(r => r.Employee)
            .Include(r => r.Tasks)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return Result.Fail("Request not found.");

        // The head returns to departments; HR / management do so only when there is no head step.
        var byHead = request.Status == ChecklistStatus.PendingFinalReview && IsFinalApprover(request);
        var byManagement = !HasFinalApprover(request) && CanManagementDecide(request);
        if (!byHead && !byManagement)
        {
            return IsFinalApprover(request) || IsManagementApprover(request)
                ? Result.Fail("Departments can only be asked to redo their tasks while the departure is waiting for your approval.")
                : Result.Fail("Request not found.");
        }
        if (string.IsNullOrWhiteSpace(reason)) return Result.Fail("Enter a reason for returning the departure.");

        var tasks = request.Tasks.Where(t => taskIds.Contains(t.Id)).OrderBy(t => t.SortOrder).ToList();
        if (tasks.Count == 0) return Result.Fail("Choose at least one department to redo its task.");
        if (tasks.Count != taskIds.Distinct().Count()) return Result.Fail("Task not found.");
        var notDone = tasks.FirstOrDefault(t => t.Status is not (ChecklistTaskStatus.Completed or ChecklistTaskStatus.NotApplicable));
        if (notDone is not null) return Result.Fail($"The {notDone.DepartmentName} task is not completed, so it cannot be returned.");

        var now = DateTime.UtcNow;
        foreach (var task in tasks) MarkReturned(task, reason, now);
        var departments = string.Join(", ", tasks.Select(t => t.DepartmentName));
        AddLog(request.Id, byHead ? ChecklistLogAction.HeadReturned : ChecklistLogAction.ManagementReturned, reason, departments);
        await RecomputeRequestStatusAsync(request, ct);
        request.ModifiedAt = now;
        await _db.SaveChangesAsync(ct);

        var returnedBy = $"{_currentUser.UserName} ({(byHead ? "department head" : "HR / management")})";
        foreach (var task in tasks) await NotifyReturnAsync(task, returnedBy, ct);
        await _audit.LogAsync("ReturnDeparture", nameof(ChecklistRequest), request.Id.ToString(),
            $"{request.RequestNo} to {departments}: {reason.Trim()}", ct);
        return Result.Success();
    }

    /// <summary>Back to the handlers: the task needs completing and approving again. The caller saves.</summary>
    private void MarkReturned(ChecklistTask task, string reason, DateTime now)
    {
        task.Status = ChecklistTaskStatus.Returned;
        task.ReturnReason = reason.Trim();
        task.ReturnedAt = now;
        task.ReturnedBy = _currentUser.UserId;
        task.ReturnedByName = _currentUser.UserName;
        task.CompletedAt = null;
        task.ApprovedBy = null;
        task.ApprovedByName = null;
        task.ApprovedAt = null;
        task.ModifiedBy = _currentUser.UserId;
        task.ModifiedAt = now;
    }

    public Task<Result> CancelAsync(int requestId, string reason, CancellationToken ct = default)
        => ConcurrencyRetry.RunAsync(_db, () => CancelOnceAsync(requestId, reason, ct));

    private async Task<Result> CancelOnceAsync(int requestId, string reason, CancellationToken ct)
    {
        var request = await _db.ChecklistRequests
            .Include(r => r.Employee)
            .Include(r => r.AccountRequest)
            .Include(r => r.Tasks)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null || !CanManage(request.Kind)) return Result.Fail("Request not found.");
        if (request.Status is ChecklistStatus.Completed or ChecklistStatus.Cancelled)
            return Result.Fail("Only requests that are not finalized or cancelled can be cancelled.");
        if (string.IsNullOrWhiteSpace(reason)) return Result.Fail("Enter a reason for cancelling.");

        var now = DateTime.UtcNow;
        var wasDispatched = request.Status != ChecklistStatus.Draft;
        request.Status = ChecklistStatus.Cancelled;
        request.CancelledAt = now;
        request.CancelledBy = _currentUser.UserId;
        request.CancelReason = reason.Trim();
        request.ModifiedAt = now;

        // Stop the linked account request too, so its items leave the handlers' My Tasks.
        if (request.AccountRequest is { Status: not (RequestStatus.Completed or RequestStatus.Closed or RequestStatus.Rejected) } accountRequest)
        {
            accountRequest.Status = RequestStatus.Closed;
            accountRequest.ModifiedBy = _currentUser.UserId;
            accountRequest.ModifiedAt = now;
        }

        AddLog(request.Id, ChecklistLogAction.Cancelled, reason);
        await _db.SaveChangesAsync(ct);

        if (wasDispatched) await NotifyCancellationAsync(request, ct);
        await _audit.LogAsync($"Cancel{request.Kind}", nameof(ChecklistRequest), request.Id.ToString(), $"{request.RequestNo}: {request.CancelReason}", ct);
        return Result.Success();
    }

    // ------------------------------------------------------------------ tasks

    /// <summary>The departments whose departure tasks the current user handles and approves.</summary>
    private sealed record TaskRoles(IReadOnlyList<int> Handles, IReadOnlyList<int> Approves)
    {
        public bool IsHandler(ChecklistTask task) => task.AssignedDepartmentId is int d && Handles.Contains(d);
        public bool IsApprover(ChecklistTask task) => task.AssignedDepartmentId is int d && Approves.Contains(d);
        public bool CanSee(ChecklistTask task) => IsHandler(task) || IsApprover(task);
    }

    private async Task<TaskRoles> GetRolesAsync(CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        return new TaskRoles(
            await _assignments.GetDepartmentIdsAsync(userId, WorkArea.Departure, AssignmentRole.Handler, ct),
            await _assignments.GetDepartmentIdsAsync(userId, WorkArea.Departure, AssignmentRole.Approver, ct));
    }

    /// <summary>Dispatched tasks of the given departments. Administrators get no view of other departments' tasks.</summary>
    private IQueryable<ChecklistTask> TasksOf(IReadOnlyList<int> departmentIds)
        => _db.ChecklistTasks.AsNoTracking()
            .Include(t => t.ChecklistRequest)!.ThenInclude(r => r!.Employee)
            .Include(t => t.Items)
            .Where(t => t.AssignedDepartmentId != null && departmentIds.Contains(t.AssignedDepartmentId.Value)
                        && t.ChecklistRequest!.Status != ChecklistStatus.Draft);

    private static IQueryable<ChecklistTask> OfOpenRequests(IQueryable<ChecklistTask> q)
        => q.Where(t => t.ChecklistRequest!.Status != ChecklistStatus.Completed && t.ChecklistRequest.Status != ChecklistStatus.Cancelled);

    private static IQueryable<ChecklistTask> Filter(IQueryable<ChecklistTask> q, ChecklistTaskHistoryQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var k = query.Keyword.Trim();
            q = q.Where(t => t.ChecklistRequest!.RequestNo.Contains(k)
                             || t.ChecklistRequest.Employee!.Name.Contains(k)
                             || t.ChecklistRequest.Employee.EmployeeNo.Contains(k));
        }
        if (query.Kind is { } kind) q = q.Where(t => t.ChecklistRequest!.Kind == kind);
        // Dates filter on when the request was dispatched.
        if (query.From is { } from) q = q.Where(t => t.ChecklistRequest!.SubmittedAt >= from.Date);
        if (query.To is { } to) q = q.Where(t => t.ChecklistRequest!.SubmittedAt < to.Date.AddDays(1));
        return q.OrderByDescending(t => t.ChecklistRequest!.SubmittedAt).ThenBy(t => t.SortOrder).Take(500);
    }

    public async Task<IReadOnlyList<ChecklistTaskDto>> GetMyPendingTasksAsync(CancellationToken ct = default)
    {
        var roles = await GetRolesAsync(ct);
        var tasks = await OfOpenRequests(TasksOf(roles.Handles))
            .Where(t => t.Status != ChecklistTaskStatus.Completed && t.Status != ChecklistTaskStatus.NotApplicable)
            .OrderBy(t => t.ChecklistRequestId).ThenBy(t => t.SortOrder)
            .ToListAsync(ct);
        return tasks.Select(t => MapTask(t, roles)).ToList();
    }

    public async Task<IReadOnlyList<ChecklistTaskDto>> GetMyTaskHistoryAsync(ChecklistTaskHistoryQuery query, CancellationToken ct = default)
    {
        var roles = await GetRolesAsync(ct);
        var q = TasksOf(roles.Handles).Where(t => t.Status == ChecklistTaskStatus.Completed
                                                  || t.Status == ChecklistTaskStatus.NotApplicable
                                                  || t.ChecklistRequest!.Status == ChecklistStatus.Completed
                                                  || t.ChecklistRequest.Status == ChecklistStatus.Cancelled);
        var tasks = await Filter(q, query).ToListAsync(ct);
        return tasks.Select(t => MapTask(t, roles)).ToList();
    }

    public async Task<IReadOnlyList<ChecklistTaskDto>> GetMyApprovalTasksAsync(CancellationToken ct = default)
    {
        var roles = await GetRolesAsync(ct);
        var tasks = await OfOpenRequests(TasksOf(roles.Approves))
            .Where(t => t.ApprovedAt == null)
            .OrderBy(t => t.ChecklistRequestId).ThenBy(t => t.SortOrder)
            .ToListAsync(ct);
        // Completed tasks waiting for a decision come first.
        return tasks.Select(t => MapTask(t, roles)).OrderByDescending(t => t.CanApprove).ToList();
    }

    public async Task<IReadOnlyList<ChecklistTaskDto>> GetMyApprovalHistoryAsync(ChecklistTaskHistoryQuery query, CancellationToken ct = default)
    {
        var roles = await GetRolesAsync(ct);
        var q = TasksOf(roles.Approves).Where(t => t.ApprovedAt != null
                                                   || t.ChecklistRequest!.Status == ChecklistStatus.Completed
                                                   || t.ChecklistRequest.Status == ChecklistStatus.Cancelled);
        var tasks = await Filter(q, query).ToListAsync(ct);
        return tasks.Select(t => MapTask(t, roles)).ToList();
    }

    public Task<Result> ApproveTaskAsync(int taskId, string? remark = null, CancellationToken ct = default)
        => ConcurrencyRetry.RunAsync(_db, () => ApproveTaskOnceAsync(taskId, remark, ct));

    private async Task<Result> ApproveTaskOnceAsync(int taskId, string? remark, CancellationToken ct)
    {
        var (task, error) = await LoadForDecisionAsync(taskId, ct);
        if (task is null) return Result.Fail(error!);

        var now = DateTime.UtcNow;
        task.ApprovedBy = _currentUser.UserId;
        task.ApprovedByName = _currentUser.UserName;
        task.ApprovedAt = now;
        task.ReturnReason = null;
        task.ReturnedAt = null;
        task.ReturnedBy = null;
        task.ReturnedByName = null;
        task.ModifiedBy = _currentUser.UserId;
        task.ModifiedAt = now;

        var statusBefore = task.ChecklistRequest!.Status;
        AddLog(task.ChecklistRequestId, ChecklistLogAction.TaskApproved, remark, task.DepartmentName);
        await RecomputeRequestStatusAsync(task.ChecklistRequest, ct);
        task.ChecklistRequest.ModifiedAt = now;
        await _db.SaveChangesAsync(ct);

        // The last department approval hands the departure to the head (or straight to HR / management).
        var request = task.ChecklistRequest;
        if (statusBefore != request.Status && request.Status == ChecklistStatus.PendingFinalReview)
            await NotifyFinalReviewDueAsync(request, ct);
        else if (statusBefore != request.Status && request.Status == ChecklistStatus.PendingManagementApproval)
            await NotifyManagementApproversAsync(request, ct);
        await _audit.LogAsync("ApproveChecklistTask", nameof(ChecklistTask), task.Id.ToString(), task.DepartmentName, ct);
        return Result.Success();
    }

    public Task<Result> ReturnTaskToHandlersAsync(int taskId, string reason, CancellationToken ct = default)
        => ConcurrencyRetry.RunAsync(_db, () => ReturnTaskToHandlersOnceAsync(taskId, reason, ct));

    private async Task<Result> ReturnTaskToHandlersOnceAsync(int taskId, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Result.Fail("Enter a reason for returning the task.");
        var (task, error) = await LoadForDecisionAsync(taskId, ct);
        if (task is null) return Result.Fail(error!);

        var now = DateTime.UtcNow;
        MarkReturned(task, reason, now);
        AddLog(task.ChecklistRequestId, ChecklistLogAction.TaskReturned, reason, task.DepartmentName);
        await RecomputeRequestStatusAsync(task.ChecklistRequest!, ct);
        task.ChecklistRequest!.ModifiedAt = now;
        await _db.SaveChangesAsync(ct);

        await NotifyReturnAsync(task, $"{_currentUser.UserName} (department approver)", ct);
        await _audit.LogAsync("ReturnChecklistTaskToHandlers", nameof(ChecklistTask), task.Id.ToString(), $"{task.DepartmentName}: {task.ReturnReason}", ct);
        return Result.Success();
    }

    /// <summary>A completed, unapproved task of an open request, for one of its department's approvers.</summary>
    private async Task<(ChecklistTask? Task, string? Error)> LoadForDecisionAsync(int taskId, CancellationToken ct)
    {
        var task = await _db.ChecklistTasks
            .Include(t => t.ChecklistRequest).ThenInclude(r => r!.Employee)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task?.ChecklistRequest is null || !(await GetRolesAsync(ct)).IsApprover(task)) return (null, "Task not found.");
        if (task.ChecklistRequest.Status is ChecklistStatus.Draft or ChecklistStatus.Completed or ChecklistStatus.Cancelled)
            return (null, "The request is not open, so this task can no longer be approved.");
        if (task.Status is not (ChecklistTaskStatus.Completed or ChecklistTaskStatus.NotApplicable))
            return (null, "The handlers have not completed this task yet.");
        if (task.ApprovedAt is not null) return (null, "This task is already approved.");
        return (task, null);
    }

    public async Task<ChecklistTaskDto?> GetTaskAsync(int taskId, CancellationToken ct = default)
    {
        var task = await _db.ChecklistTasks.AsNoTracking()
            .Include(t => t.ChecklistRequest)!.ThenInclude(r => r!.Employee)
            .Include(t => t.Items.OrderBy(i => i.SortOrder))
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null || task.ChecklistRequest?.Status == ChecklistStatus.Draft) return null;
        var roles = await GetRolesAsync(ct);
        return roles.CanSee(task) ? MapTask(task, roles) : null;
    }

    public Task<Result> UpdateTaskAsync(ChecklistTaskUpdateDto dto, CancellationToken ct = default)
        => ConcurrencyRetry.RunAsync(_db, () => UpdateTaskOnceAsync(dto, ct));

    private async Task<Result> UpdateTaskOnceAsync(ChecklistTaskUpdateDto dto, CancellationToken ct)
    {
        var task = await _db.ChecklistTasks
            .Include(t => t.ChecklistRequest)
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == dto.TaskId, ct);
        if (task is null) return Result.Fail("Task not found.");
        if (!(await GetRolesAsync(ct)).CanSee(task)) return Result.Fail("You are not allowed to update this task.");
        if (task.ChecklistRequest is null || task.ChecklistRequest.Status is ChecklistStatus.Draft or ChecklistStatus.Completed or ChecklistStatus.Cancelled)
            return Result.Fail("Request already closed.");
        if (task.Status is ChecklistTaskStatus.Completed or ChecklistTaskStatus.NotApplicable)
            return Result.Fail("This task is already completed. If it needs changes, ask the department approver to return it.");

        if (dto.Items.Any(i => i.IsCompleted && i.IsNotApplicable))
            return Result.Fail("A checklist item can be either Done or N/A, not both.");

        // Validate every upload before changing anything.
        var uploads = new Dictionary<int, (ChecklistAttachmentUpload File, string ContentType)>();
        foreach (var updateItem in dto.Items.Where(i => i.NewAttachment is not null))
        {
            var item = task.Items.FirstOrDefault(i => i.Id == updateItem.Id);
            if (item is null) continue;
            if (!item.AllowAttachment) return Result.Fail($"\"{item.Description}\" does not take an attachment.");
            var (contentType, error) = ChecklistAttachmentRules.Validate(updateItem.NewAttachment!.FileName, updateItem.NewAttachment.Content);
            if (error is not null) return Result.Fail(error);
            uploads[item.Id] = (updateItem.NewAttachment, contentType!);
        }

        // Load the content rows only for the items whose attachment changes.
        var changingIds = dto.Items.Where(i => i.NewAttachment is not null || i.RemoveAttachment).Select(i => i.Id).ToList();
        if (changingIds.Count > 0)
        {
            await _db.ChecklistTaskItems.Where(i => i.ChecklistTaskId == task.Id && changingIds.Contains(i.Id))
                .Include(i => i.Attachment)
                .LoadAsync(ct);
        }

        var now = DateTime.UtcNow;
        foreach (var updateItem in dto.Items)
        {
            var item = task.Items.FirstOrDefault(i => i.Id == updateItem.Id);
            if (item is null) continue;

            item.IsNotApplicable = updateItem.IsNotApplicable;
            item.IsCompleted = updateItem.IsCompleted;
            item.Remark = updateItem.Remark;

            if (uploads.TryGetValue(item.Id, out var upload))
            {
                if (item.Attachment is null) item.Attachment = new ChecklistTaskItemAttachment { Content = upload.File.Content };
                else item.Attachment.Content = upload.File.Content;
                item.AttachmentFileName = TrimFileName(upload.File.FileName);
                item.AttachmentContentType = upload.ContentType;
                item.AttachmentSize = upload.File.Content.Length;
            }
            else if (updateItem.RemoveAttachment && item.AttachmentFileName is not null)
            {
                item.Attachment = null;
                item.AttachmentFileName = null;
                item.AttachmentContentType = null;
                item.AttachmentSize = null;
            }
            if (item.IsCompleted || item.IsNotApplicable)
            {
                item.CompletedBy = _currentUser.UserId;
                item.CompletedAt = now;
            }
            else
            {
                item.CompletedBy = null;
                item.CompletedAt = null;
            }
        }

        task.TaskRemark = dto.TaskRemark;
        task.HandledBy = _currentUser.UserId;

        var hasProgress = task.Items.Any(i => i.IsCompleted || i.IsNotApplicable || !string.IsNullOrWhiteSpace(i.Remark)
                                              || i.AttachmentFileName is not null);
        var allRequiredDone = task.Items.Where(i => i.IsRequired).All(i => i.IsCompleted || i.IsNotApplicable);

        if (dto.MarkAsCompleted)
        {
            if (task.ChecklistRequest.Kind == ChecklistKind.Departure && task.Items.Any(i => !i.IsCompleted && !i.IsNotApplicable))
                return Result.Fail("Mark every checklist item as either Done or N/A before completing the department task.");
            if (!allRequiredDone)
                return Result.Fail("Complete all required checklist items before submitting the department task.");

            var requiredItems = task.Items.Where(i => i.IsRequired).ToList();
            task.Status = requiredItems.Count > 0 && requiredItems.All(i => i.IsNotApplicable)
                ? ChecklistTaskStatus.NotApplicable
                : ChecklistTaskStatus.Completed;
            // The return note stays until the approver approves, so they can check what was fixed.
            task.CompletedAt = now;
        }
        else
        {
            task.Status = hasProgress ? ChecklistTaskStatus.InProgress : ChecklistTaskStatus.NotStarted;
            task.CompletedAt = null;
        }

        if (dto.MarkAsCompleted) AddLog(task.ChecklistRequestId, ChecklistLogAction.TaskCompleted, task.TaskRemark, task.DepartmentName);

        // Always write the request row so its row version detects a parallel update by another
        // department; the retry then recomputes from the tasks that department just changed.
        await RecomputeRequestStatusAsync(task.ChecklistRequest, ct);
        task.ChecklistRequest.ModifiedAt = now;
        await _db.SaveChangesAsync(ct);

        if (dto.MarkAsCompleted) await NotifyApproversAsync(task, ct);
        await _audit.LogAsync("UpdateChecklistTask", nameof(ChecklistTask), task.Id.ToString(), task.Status.ToString(), ct);
        return Result.Success();
    }

    /// <summary>
    /// Once every required department task is completed and approved, the departure goes to the
    /// department head, or straight to HR / management when the leaver is that head.
    /// </summary>
    private async Task RecomputeRequestStatusAsync(ChecklistRequest request, CancellationToken ct)
    {
        var tasks = await _db.ChecklistTasks.Where(t => t.ChecklistRequestId == request.Id).ToListAsync(ct);
        if (tasks.Count == 0)
        {
            request.Status = ChecklistStatus.Submitted;
            return;
        }

        var requiredTasks = tasks.Where(t => t.IsRequired).ToList();
        if (requiredTasks.All(t => t.Status is ChecklistTaskStatus.Completed or ChecklistTaskStatus.NotApplicable && t.ApprovedAt is not null))
        {
            // Already with the head or HR / management: an optional task changing does not restart the approvals.
            if (request.Status is not (ChecklistStatus.PendingFinalReview or ChecklistStatus.PendingManagementApproval))
                request.Status = HasFinalApprover(request) ? ChecklistStatus.PendingFinalReview : ChecklistStatus.PendingManagementApproval;
            return;
        }

        var anyProgress = tasks.Any(t => t.Status is ChecklistTaskStatus.InProgress or ChecklistTaskStatus.Completed
                                                      or ChecklistTaskStatus.Returned or ChecklistTaskStatus.NotApplicable);
        request.Status = anyProgress ? ChecklistStatus.InProgress : ChecklistStatus.Submitted;
    }

    /// <summary>
    /// An item's attachment, for the department's handlers and approvers, the department head, HR /
    /// management approvers, and holders of the kind's manage permission.
    /// </summary>
    public async Task<ChecklistAttachmentFile?> GetItemAttachmentAsync(int itemId, CancellationToken ct = default)
    {
        var item = await _db.ChecklistTaskItems.AsNoTracking()
            .Where(i => i.Id == itemId && i.AttachmentFileName != null)
            .Select(i => new
            {
                i.AttachmentFileName,
                i.AttachmentContentType,
                Task = i.ChecklistTask,
                Request = i.ChecklistTask!.ChecklistRequest
            })
            .FirstOrDefaultAsync(ct);
        if (item?.Request is null || item.Task is null) return null;
        if (!IsFinalApprover(item.Request) && !IsManagementApprover(item.Request) && !CanManage(item.Request.Kind)
            && !(await GetRolesAsync(ct)).CanSee(item.Task)) return null;

        var content = await _db.ChecklistTaskItems.AsNoTracking()
            .Where(i => i.Id == itemId)
            .Select(i => i.Attachment!.Content)
            .FirstOrDefaultAsync(ct);
        return content is null
            ? null
            : new ChecklistAttachmentFile(item.AttachmentFileName!, item.AttachmentContentType ?? "application/octet-stream", content);
    }

    private static string TrimFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/')).Trim();
        if (name.Length <= 255) return name;
        var extension = Path.GetExtension(name);
        return name[..(255 - extension.Length)] + extension;
    }

    // ---------------------------------------------------------- notifications

    private async Task<IReadOnlyList<string>> EmailsOfAsync(IEnumerable<int?> departmentIds, AssignmentRole role, CancellationToken ct)
    {
        var assignees = await _assignments.GetAssigneesAsync(
            departmentIds.Where(d => d is not null).Select(d => d!.Value), WorkArea.Departure, role, ct);
        return assignees.Values.SelectMany(u => u)
            .Select(u => u.Email)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!)
            .Distinct()
            .ToList();
    }

    private async Task NotifyFinalReviewDueAsync(ChecklistRequest request, CancellationToken ct)
    {
        if (!HasFinalApprover(request)) return;
        var approver = await _users.GetByIdAsync(request.FinalApproverUserId!, ct);
        if (string.IsNullOrWhiteSpace(approver?.Email)) return;

        var employee = await _db.Employees.AsNoTracking().Where(e => e.Id == request.EmployeeId)
            .Select(e => new { e.Name, e.EmployeeNo }).FirstOrDefaultAsync(ct);
        _email.Enqueue(new EmailMessage(
            new[] { approver.Email },
            $"[Action Required] Departure final review ({request.RequestNo})",
            $"<p>All departments have completed and approved the departure checklist for <b>{employee?.Name}</b> ({employee?.EmployeeNo}).</p>" +
            "<p>Please review it as the employee's department head in HR System under Departure Approval. " +
            "After your approval it goes to HR / management.</p>"));
    }

    /// <summary>Tell HR / management (departures.approve) that a departure waits for their approval.</summary>
    private async Task NotifyManagementApproversAsync(ChecklistRequest request, CancellationToken ct)
    {
        var leaverUserId = request.Employee?.UserId
            ?? await _db.Employees.AsNoTracking().Where(e => e.Id == request.EmployeeId).Select(e => e.UserId).FirstOrDefaultAsync(ct);
        var emails = (await _users.GetUsersWithPermissionAsync(Permissions.DeparturesApprove, ct))
            .Where(u => u.UserId != leaverUserId && !string.IsNullOrWhiteSpace(u.Email))
            .Select(u => u.Email!)
            .Distinct()
            .ToList();
        if (emails.Count == 0) return;

        var employee = await _db.Employees.AsNoTracking().Where(e => e.Id == request.EmployeeId)
            .Select(e => new { e.Name, e.EmployeeNo }).FirstOrDefaultAsync(ct);
        var headLine = HasFinalApprover(request)
            ? $"{System.Net.WebUtility.HtmlEncode(request.FinalApproverUserName)}, the employee's department head, approved it."
            : "All departments have completed and approved it.";
        _email.Enqueue(new EmailMessage(
            emails,
            $"[Approval Required] Departure ({request.RequestNo}) - HR / management approval",
            $"<p>The departure of <b>{employee?.Name}</b> ({employee?.EmployeeNo}) is waiting for HR / management approval. {headLine}</p>" +
            "<p>Please approve or return it in HR System under Departure Approval.</p>"));
    }

    private async Task NotifyHeadOfReturnAsync(ChecklistRequest request, string reason, CancellationToken ct)
    {
        var head = await _users.GetByIdAsync(request.FinalApproverUserId!, ct);
        if (string.IsNullOrWhiteSpace(head?.Email)) return;

        _email.Enqueue(new EmailMessage(
            new[] { head.Email },
            $"[Returned] Departure ({request.RequestNo})",
            $"<p>{System.Net.WebUtility.HtmlEncode(_currentUser.UserName)} (HR / management) returned the departure of " +
            $"<b>{request.Employee?.Name}</b> ({request.Employee?.EmployeeNo}) to you.</p>" +
            $"<p>Reason: {System.Net.WebUtility.HtmlEncode(reason.Trim())}</p>" +
            "<p>Approve it again, or return it to the departments that must redo their tasks, under Departure Approval.</p>"));
    }

    /// <summary>Tell the department's approvers that a task is waiting for their approval.</summary>
    private async Task NotifyApproversAsync(ChecklistTask task, CancellationToken ct)
    {
        var emails = await EmailsOfAsync(new[] { task.AssignedDepartmentId }, AssignmentRole.Approver, ct);
        if (emails.Count == 0) return;

        var employee = await _db.Employees.AsNoTracking().Where(e => e.Id == task.ChecklistRequest!.EmployeeId)
            .Select(e => new { e.Name, e.EmployeeNo }).FirstOrDefaultAsync(ct);
        _email.Enqueue(new EmailMessage(
            emails,
            $"[Approval Required] Departure checklist ({task.ChecklistRequest!.RequestNo}) - {task.DepartmentName}",
            $"<p>The {System.Net.WebUtility.HtmlEncode(task.DepartmentName)} departure checklist for <b>{employee?.Name}</b> ({employee?.EmployeeNo}) is completed.</p>" +
            "<p>Please approve it, or return it to the handlers, in HR System under Departure Approval.</p>"));
    }

    private async Task NotifyReturnAsync(ChecklistTask task, string returnedBy, CancellationToken ct)
    {
        var emails = await EmailsOfAsync(new[] { task.AssignedDepartmentId }, AssignmentRole.Handler, ct);
        if (emails.Count == 0) return;

        var request = task.ChecklistRequest!;
        _email.Enqueue(new EmailMessage(
            emails,
            $"[Returned] Departure checklist ({request.RequestNo}) - {task.DepartmentName}",
            $"<p>{System.Net.WebUtility.HtmlEncode(returnedBy)} returned the {System.Net.WebUtility.HtmlEncode(task.DepartmentName)} task for " +
            $"<b>{request.Employee?.Name}</b> ({request.Employee?.EmployeeNo}).</p>" +
            $"<p>Reason: {System.Net.WebUtility.HtmlEncode(task.ReturnReason)}</p><p>Please update it under Departure Checklist.</p>"));
    }

    /// <summary>Tell the submitter and the department head that HR / management approved the departure.</summary>
    private async Task NotifyCompletionAsync(ChecklistRequest request, string remark, CancellationToken ct)
    {
        var userIds = new[] { request.SubmittedBy, request.FinalApproverUserId }.Where(id => id is not null).Select(id => id!);
        var emails = (await _users.GetByIdsAsync(userIds, ct)).Values
            .Select(u => u.Email)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!)
            .Distinct()
            .ToList();
        if (emails.Count == 0) return;

        _email.Enqueue(new EmailMessage(
            emails,
            $"[Completed] Departure ({request.RequestNo})",
            $"<p>{System.Net.WebUtility.HtmlEncode(_currentUser.UserName)} (HR / management) approved the departure of " +
            $"<b>{request.Employee?.Name}</b> ({request.Employee?.EmployeeNo}). The employee is now marked as resigned.</p>" +
            $"<p>Remark: {System.Net.WebUtility.HtmlEncode(remark.Trim())}</p>"));
    }

    private async Task NotifyCancellationAsync(ChecklistRequest request, CancellationToken ct)
    {
        var label = Label(request.Kind);
        var openDepartments = request.Tasks.Where(t => t.ApprovedAt is null).Select(t => t.AssignedDepartmentId).ToList();
        var emails = (await EmailsOfAsync(openDepartments, AssignmentRole.Handler, ct))
            .Concat(await EmailsOfAsync(openDepartments, AssignmentRole.Approver, ct))
            .Distinct()
            .ToList();
        if (emails.Count == 0) return;

        _email.Enqueue(new EmailMessage(
            emails,
            $"[Cancelled] {char.ToUpper(label[0]) + label[1..]} checklist ({request.RequestNo})",
            $"<p>The {label} checklist for <b>{request.Employee?.Name}</b> ({request.Employee?.EmployeeNo}) was cancelled. No further action is needed.</p>" +
            $"<p>Reason: {System.Net.WebUtility.HtmlEncode(request.CancelReason)}</p>"));
    }

    /// <summary>One email per department to its handlers.</summary>
    private async Task NotifyHandlersAsync(ChecklistRequest request, IReadOnlyList<ChecklistTask> tasks, CancellationToken ct)
    {
        foreach (var task in tasks)
        {
            var emails = await EmailsOfAsync(new[] { task.AssignedDepartmentId }, AssignmentRole.Handler, ct);
            if (emails.Count == 0) continue;
            _email.Enqueue(new EmailMessage(
                emails,
                $"[Action Required] Departure checklist ({request.RequestNo}) - {task.DepartmentName}",
                $"<p>There is a new {System.Net.WebUtility.HtmlEncode(task.DepartmentName)} departure checklist for <b>{request.Employee?.Name}</b> ({request.Employee?.EmployeeNo}).</p>" +
                "<p>Please process it in HR System under Departure Checklist.</p>"));
        }
    }

    private static ChecklistRequestDto MapRequest(ChecklistRequest request) => new()
    {
        Id = request.Id,
        Kind = request.Kind,
        RequestNo = request.RequestNo,
        EmployeeId = request.EmployeeId,
        EmployeeNo = request.Employee?.EmployeeNo ?? string.Empty,
        EmployeeName = request.Employee?.Name ?? string.Empty,
        DepartmentName = request.Employee?.Department?.Name,
        Position = request.Employee?.Position,
        EmployeeCategory = request.Employee?.Category ?? default,
        EmployeeEmail = request.Employee?.Email,
        EmployeeStatus = request.Employee?.Status ?? default,
        JoinDate = request.Employee?.JoinDate,
        Status = request.Status,
        StartDate = request.StartDate,
        LastWorkingDate = request.LastWorkingDate,
        LastEmploymentDate = request.LastEmploymentDate,
        Reason = request.Reason,
        Remark = request.Remark,
        SubmittedAt = request.SubmittedAt,
        FinalizedAt = request.FinalizedAt,
        CompletedAt = request.CompletedAt,
        CancelledAt = request.CancelledAt,
        CancelReason = request.CancelReason,
        FinalApproverUserId = request.FinalApproverUserId,
        FinalApproverName = request.FinalApproverUserName,
        AccountRequestId = request.AccountRequestId,
        AccountRequestNo = request.AccountRequest?.RequestNo,
        AccountRequestStatus = request.AccountRequest?.Status,
        Tasks = request.Tasks.OrderBy(t => t.SortOrder).Select(t => MapTask(t, null)).ToList(),
        Logs = request.Logs.OrderBy(l => l.At).ThenBy(l => l.Id).Select(l => new ChecklistLogDto
        {
            Action = l.Action,
            Departments = l.Departments,
            Remark = l.Remark,
            UserName = l.UserName,
            At = l.At
        }).ToList()
    };

    /// <summary>With <paramref name="roles"/>, the DTO also says what the current user may do with the task.</summary>
    private static ChecklistTaskDto MapTask(ChecklistTask task, TaskRoles? roles) => new()
    {
        Id = task.Id,
        ChecklistRequestId = task.ChecklistRequestId,
        Kind = task.ChecklistRequest?.Kind ?? default,
        RequestNo = task.ChecklistRequest?.RequestNo ?? string.Empty,
        EmployeeNo = task.ChecklistRequest?.Employee?.EmployeeNo ?? string.Empty,
        EmployeeName = task.ChecklistRequest?.Employee?.Name ?? string.Empty,
        DepartmentName = task.DepartmentName,
        Status = task.Status,
        IsRequired = task.IsRequired,
        SortOrder = task.SortOrder,
        TaskRemark = task.TaskRemark,
        CompletedAt = task.CompletedAt,
        ReturnReason = task.ReturnReason,
        ReturnedAt = task.ReturnedAt,
        ReturnedByName = task.ReturnedByName,
        ApprovedByName = task.ApprovedByName,
        ApprovedAt = task.ApprovedAt,
        IsHandler = roles?.IsHandler(task) ?? false,
        IsApprover = roles?.IsApprover(task) ?? false,
        RequestStatus = task.ChecklistRequest?.Status ?? default,
        Items = task.Items.OrderBy(i => i.SortOrder).Select(i => new ChecklistTaskItemDto
        {
            Id = i.Id,
            Description = i.Description,
            SortOrder = i.SortOrder,
            IsRequired = i.IsRequired,
            IsCompleted = i.IsCompleted,
            IsNotApplicable = i.IsNotApplicable,
            Remark = i.Remark,
            AllowAttachment = i.AllowAttachment,
            AttachmentFileName = i.AttachmentFileName,
            AttachmentSize = i.AttachmentSize
        }).ToList()
    };
}
