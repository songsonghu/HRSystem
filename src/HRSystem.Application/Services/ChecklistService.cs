using HRSystem.Application.Common;
using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using HRSystem.Domain.Entities;
using HRSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Services;

/// <summary>
/// Onboarding and departure workflow: draft -> submit (one task per active template
/// department, assigned to its configured approver or else its manager) -> departments complete checklists in parallel
/// -> pending final review -> finalize. A departure is finalized by the head of the leaver's department, who may
/// return tasks; HR finalizes onboarding and departures of department heads themselves.
/// </summary>
public class ChecklistService : IChecklistService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IEmailService _email;
    private readonly IUserDirectoryService _users;
    private readonly IAccountRequestService _accountRequests;

    public ChecklistService(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditService audit,
        IEmailService email,
        IUserDirectoryService users,
        IAccountRequestService accountRequests)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _email = email;
        _users = users;
        _accountRequests = accountRequests;
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
            .Include(r => r.Employee)
            .Include(r => r.AccountRequest)
            .Include(r => r.Tasks.OrderBy(t => t.SortOrder))
            .ThenInclude(t => t.Items.OrderBy(i => i.SortOrder))
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);

        return request is null || !CanManage(request.Kind) ? null : MapRequest(request);
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
        var templates = await _db.ChecklistTemplates.AsNoTracking()
            .Where(t => t.Kind == kind && t.IsActive && !t.IsDeleted)
            .OrderBy(t => t.SortOrder)
            .Select(t => new
            {
                Dto = new ChecklistTemplatePreviewDto
                {
                    DepartmentName = t.Department!.Name,
                    ItemCount = t.Items.Count(i => !i.IsDeleted)
                },
                AssigneeUserId = t.ApproverUserId ?? t.Department.Manager!.UserId
            })
            .ToListAsync(ct);

        var users = await _users.GetByIdsAsync(templates.Where(t => t.AssigneeUserId != null).Select(t => t.AssigneeUserId!), ct);
        foreach (var t in templates)
        {
            t.Dto.AssigneeName = t.AssigneeUserId is not null && users.TryGetValue(t.AssigneeUserId, out var user) ? user.Display : null;
            t.Dto.CanReceiveTasks = t.Dto.AssigneeName is not null;
        }
        return templates.Select(t => t.Dto).ToList();
    }

    /// <summary>The configured approver, or else the department manager's login.</summary>
    private static string? AssigneeOf(ChecklistTemplate template) => template.ApproverUserId ?? template.Department?.Manager?.UserId;

    // -------------------------------------------------------- submit/finalize

    public async Task<Result> SubmitAsync(int requestId, CancellationToken ct = default)
    {
        var snapshot = await _db.ChecklistRequests.AsNoTracking()
            .Include(r => r.AccountRequest)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (snapshot is null || !CanManage(snapshot.Kind)) return Result.Fail("Request not found.");
        if (snapshot.Status != ChecklistStatus.Draft) return Result.Fail("Only draft requests can be submitted.");

        var templates = await _db.ChecklistTemplates.AsNoTracking()
            .Include(t => t.Items)
            .Include(t => t.Department).ThenInclude(d => d!.Manager)
            .Where(t => t.Kind == snapshot.Kind && t.IsActive && !t.IsDeleted)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(ct);
        if (templates.Count == 0) return Result.Fail($"No active {Label(snapshot.Kind)} checklist templates found.");

        // Check every department can receive its task before anything is dispatched.
        var unrouted = templates.Where(t => AssigneeOf(t) is null).Select(t => t.Department!.Name).ToList();
        if (unrouted.Count > 0)
            return Result.Fail($"No approver is set for: {string.Join(", ", unrouted)}. Set one under Settings > Checklist Approvers, or give the department a manager with a login.");

        var assignees = new Dictionary<string, UserDirectoryEntry>();
        foreach (var userId in templates.Select(t => AssigneeOf(t)!).Distinct())
        {
            var user = await _users.GetByIdAsync(userId, ct);
            if (string.IsNullOrWhiteSpace(user?.Email))
                return Result.Fail($"The approver login of {templates.First(t => AssigneeOf(t) == userId).Department!.Name} has no email.");
            assignees[userId] = user;
        }

        UserDirectoryEntry? finalApprover = null;
        if (snapshot.Kind == ChecklistKind.Departure)
        {
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
            var assignee = assignees[AssigneeOf(template)!];
            var task = new ChecklistTask
            {
                ChecklistRequestId = request.Id,
                AssignedDepartmentId = template.DepartmentId,
                DepartmentName = template.Department!.Name,
                AssignedUserId = assignee.UserId,
                AssignedUserName = assignee.UserName,
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
        await _db.SaveChangesAsync(ct);

        NotifyAssignees(request, tasks, assignees);
        await _audit.LogAsync($"Submit{request.Kind}", nameof(ChecklistRequest), request.Id.ToString(), request.RequestNo, ct);
        return Result.Success();
    }

    public Task<Result> FinalizeAsync(int requestId, CancellationToken ct = default)
        => ConcurrencyRetry.RunAsync(_db, () => FinalizeOnceAsync(requestId, ct));

    private async Task<Result> FinalizeOnceAsync(int requestId, CancellationToken ct)
    {
        var request = await _db.ChecklistRequests
            .Include(r => r.Employee)
            .Include(r => r.AccountRequest)
            .Include(r => r.Tasks)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return Result.Fail("Request not found.");
        var byFinalApprover = HasFinalApprover(request);
        if (byFinalApprover ? !IsFinalApprover(request) : !CanManage(request.Kind))
        {
            return byFinalApprover && CanManage(request.Kind)
                ? Result.Fail($"This departure is approved by {request.FinalApproverUserName}, the head of the employee's department.")
                : Result.Fail("Request not found.");
        }
        if (request.Status == ChecklistStatus.Completed) return Result.Fail("This request is already finalized.");
        if (request.Status == ChecklistStatus.Cancelled) return Result.Fail("This request was cancelled.");

        var requiredTasks = request.Tasks.Where(t => t.IsRequired).ToList();
        if (requiredTasks.Count == 0)
            return Result.Fail("This request has no required department tasks.");
        if (requiredTasks.Any(t => t.Status is not (ChecklistTaskStatus.Completed or ChecklistTaskStatus.NotApplicable)))
            return Result.Fail("All required department tasks must be completed before finalization.");
        if (request.AccountRequest is not null && request.AccountRequest.Status != RequestStatus.Completed)
            return Result.Fail($"Account request {request.AccountRequest.RequestNo} is not completed yet.");

        request.Status = ChecklistStatus.Completed;
        request.FinalizedAt = DateTime.UtcNow;
        request.FinalizedBy = _currentUser.UserId;
        request.CompletedAt = request.FinalizedAt;

        if (request.Kind == ChecklistKind.Departure && request.Employee is not null)
        {
            request.Employee.Status = EmployeeStatus.Resigned;
            request.Employee.ResignDate = request.LastEmploymentDate;
            request.Employee.ModifiedBy = _currentUser.UserId;
            request.Employee.ModifiedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        if (byFinalApprover) await NotifySubmitterOfApprovalAsync(request, ct);
        await _audit.LogAsync($"Finalize{request.Kind}", nameof(ChecklistRequest), request.Id.ToString(), request.RequestNo, ct);
        return Result.Success();
    }

    private static bool HasFinalApprover(ChecklistRequest request)
        => request.Kind == ChecklistKind.Departure && request.FinalApproverUserId is not null;

    private bool IsFinalApprover(ChecklistRequest request)
        => HasFinalApprover(request) && !string.IsNullOrWhiteSpace(_currentUser.UserId) && request.FinalApproverUserId == _currentUser.UserId;

    // ---------------------------------------------------------- final review

    private IQueryable<ChecklistRequest> MyFinalReviews()
    {
        var userId = _currentUser.UserId;
        return _db.ChecklistRequests.AsNoTracking()
            .Include(r => r.Employee)
            .Include(r => r.AccountRequest)
            .Include(r => r.Tasks)
            .Where(r => userId != null && r.Kind == ChecklistKind.Departure && r.FinalApproverUserId == userId);
    }

    public async Task<IReadOnlyList<ChecklistRequestDto>> GetMyFinalReviewsAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId)) return Array.Empty<ChecklistRequestDto>();

        var requests = await MyFinalReviews()
            .Where(r => r.Status == ChecklistStatus.PendingFinalReview)
            .OrderBy(r => r.SubmittedAt)
            .ToListAsync(ct);
        return requests.Select(MapRequest).ToList();
    }

    public async Task<IReadOnlyList<ChecklistRequestDto>> GetMyFinalReviewHistoryAsync(ChecklistTaskHistoryQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId)) return Array.Empty<ChecklistRequestDto>();
        if (query.Kind == ChecklistKind.Onboarding) return Array.Empty<ChecklistRequestDto>();

        var q = MyFinalReviews().Where(r => r.Status != ChecklistStatus.PendingFinalReview);
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
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        return request is null || !IsFinalApprover(request) ? null : MapRequest(request);
    }

    public Task<Result> ReturnTaskAsync(int taskId, string reason, CancellationToken ct = default)
        => ConcurrencyRetry.RunAsync(_db, () => ReturnTaskOnceAsync(taskId, reason, ct));

    private async Task<Result> ReturnTaskOnceAsync(int taskId, string reason, CancellationToken ct)
    {
        var task = await _db.ChecklistTasks
            .Include(t => t.ChecklistRequest).ThenInclude(r => r!.Employee)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task?.ChecklistRequest is null || !IsFinalApprover(task.ChecklistRequest)) return Result.Fail("Task not found.");
        if (task.ChecklistRequest.Status != ChecklistStatus.PendingFinalReview)
            return Result.Fail("Tasks can only be returned while the request is waiting for final review.");
        if (task.Status is not (ChecklistTaskStatus.Completed or ChecklistTaskStatus.NotApplicable))
            return Result.Fail("Only completed tasks can be returned.");
        if (string.IsNullOrWhiteSpace(reason)) return Result.Fail("Enter a reason for returning the task.");

        var now = DateTime.UtcNow;
        task.Status = ChecklistTaskStatus.Returned;
        task.ReturnReason = reason.Trim();
        task.ReturnedAt = now;
        task.CompletedAt = null;
        task.ModifiedBy = _currentUser.UserId;
        task.ModifiedAt = now;

        await RecomputeRequestStatusAsync(task.ChecklistRequest, ct);
        task.ChecklistRequest.ModifiedAt = now;
        await _db.SaveChangesAsync(ct);

        await NotifyReturnAsync(task, ct);
        await _audit.LogAsync("ReturnChecklistTask", nameof(ChecklistTask), task.Id.ToString(), $"{task.DepartmentName}: {task.ReturnReason}", ct);
        return Result.Success();
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

        await _db.SaveChangesAsync(ct);

        if (wasDispatched) await NotifyCancellationAsync(request, ct);
        await _audit.LogAsync($"Cancel{request.Kind}", nameof(ChecklistRequest), request.Id.ToString(), $"{request.RequestNo}: {request.CancelReason}", ct);
        return Result.Success();
    }

    // ------------------------------------------------------------------ tasks

    private IQueryable<ChecklistTask> MyTasks()
    {
        // Tasks belong to their assignee alone; administrators get no view of other people's tasks.
        var userId = _currentUser.UserId;
        return _db.ChecklistTasks.AsNoTracking()
            .Include(t => t.ChecklistRequest)!.ThenInclude(r => r!.Employee)
            .Include(t => t.Items)
            .Where(t => userId != null && t.AssignedUserId == userId && t.ChecklistRequest!.Status != ChecklistStatus.Draft);
    }

    public async Task<IReadOnlyList<ChecklistTaskDto>> GetMyPendingTasksAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId)) return Array.Empty<ChecklistTaskDto>();

        var tasks = await MyTasks()
            .Where(t => t.ChecklistRequest!.Status != ChecklistStatus.Completed
                        && t.ChecklistRequest.Status != ChecklistStatus.Cancelled
                        && t.Status != ChecklistTaskStatus.Completed
                        && t.Status != ChecklistTaskStatus.NotApplicable)
            .OrderBy(t => t.ChecklistRequestId).ThenBy(t => t.SortOrder)
            .ToListAsync(ct);
        return tasks.Select(MapTask).ToList();
    }

    public async Task<IReadOnlyList<ChecklistTaskDto>> GetMyTaskHistoryAsync(ChecklistTaskHistoryQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId)) return Array.Empty<ChecklistTaskDto>();

        var q = MyTasks().Where(t => t.Status == ChecklistTaskStatus.Completed
                                     || t.Status == ChecklistTaskStatus.NotApplicable
                                     || t.ChecklistRequest!.Status == ChecklistStatus.Completed
                                     || t.ChecklistRequest.Status == ChecklistStatus.Cancelled);

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

        var tasks = await q.OrderByDescending(t => t.ChecklistRequest!.SubmittedAt).ThenBy(t => t.SortOrder)
            .Take(500)
            .ToListAsync(ct);
        return tasks.Select(MapTask).ToList();
    }

    public Task<Result> ReopenTaskAsync(int taskId, CancellationToken ct = default)
        => ConcurrencyRetry.RunAsync(_db, () => ReopenTaskOnceAsync(taskId, ct));

    private async Task<Result> ReopenTaskOnceAsync(int taskId, CancellationToken ct)
    {
        var task = await _db.ChecklistTasks
            .Include(t => t.ChecklistRequest)
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null || !CanHandleTask(task.AssignedUserId)) return Result.Fail("Task not found.");
        if (task.ChecklistRequest is null || task.ChecklistRequest.Status is ChecklistStatus.Completed or ChecklistStatus.Cancelled)
            return Result.Fail("The request is already finalized or cancelled, so this task can no longer be changed.");
        if (task.Status is not (ChecklistTaskStatus.Completed or ChecklistTaskStatus.NotApplicable))
            return Result.Fail("Only submitted tasks can be taken back.");

        task.Status = ChecklistTaskStatus.InProgress;
        task.CompletedAt = null;
        task.HandledBy = _currentUser.UserId;

        await RecomputeRequestStatusAsync(task.ChecklistRequest, ct);
        task.ChecklistRequest.ModifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("ReopenChecklistTask", nameof(ChecklistTask), task.Id.ToString(), task.DepartmentName, ct);
        return Result.Success();
    }

    public async Task<ChecklistTaskDto?> GetTaskAsync(int taskId, CancellationToken ct = default)
    {
        var task = await _db.ChecklistTasks.AsNoTracking()
            .Include(t => t.ChecklistRequest)!.ThenInclude(r => r!.Employee)
            .Include(t => t.Items.OrderBy(i => i.SortOrder))
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null || !CanHandleTask(task.AssignedUserId)) return null;
        return MapTask(task);
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
        if (!CanHandleTask(task.AssignedUserId)) return Result.Fail("You are not allowed to update this task.");
        if (task.ChecklistRequest is null || task.ChecklistRequest.Status is ChecklistStatus.Completed or ChecklistStatus.Cancelled)
            return Result.Fail("Request already closed.");
        if (task.Status is ChecklistTaskStatus.Completed or ChecklistTaskStatus.NotApplicable)
            return Result.Fail("This task is already submitted. Take it back first to change it.");

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
            task.CompletedAt = now;
            task.ReturnReason = null;
            task.ReturnedAt = null;
        }
        else
        {
            task.Status = hasProgress ? ChecklistTaskStatus.InProgress : ChecklistTaskStatus.NotStarted;
            task.CompletedAt = null;
        }

        // Always write the request row so its row version detects a parallel update by another
        // department; the retry then recomputes from the tasks that department just changed.
        var statusBefore = task.ChecklistRequest.Status;
        await RecomputeRequestStatusAsync(task.ChecklistRequest, ct);
        task.ChecklistRequest.ModifiedAt = now;
        await _db.SaveChangesAsync(ct);

        if (statusBefore != ChecklistStatus.PendingFinalReview && task.ChecklistRequest.Status == ChecklistStatus.PendingFinalReview)
            await NotifyFinalReviewDueAsync(task.ChecklistRequest, ct);
        await _audit.LogAsync("UpdateChecklistTask", nameof(ChecklistTask), task.Id.ToString(), task.Status.ToString(), ct);
        return Result.Success();
    }

    private async Task RecomputeRequestStatusAsync(ChecklistRequest request, CancellationToken ct)
    {
        var tasks = await _db.ChecklistTasks.Where(t => t.ChecklistRequestId == request.Id).ToListAsync(ct);
        if (tasks.Count == 0)
        {
            request.Status = ChecklistStatus.Submitted;
            return;
        }

        var requiredTasks = tasks.Where(t => t.IsRequired).ToList();
        if (requiredTasks.All(t => t.Status is ChecklistTaskStatus.Completed or ChecklistTaskStatus.NotApplicable))
        {
            request.Status = ChecklistStatus.PendingFinalReview;
            return;
        }

        var anyProgress = tasks.Any(t => t.Status is ChecklistTaskStatus.InProgress or ChecklistTaskStatus.Completed
                                                      or ChecklistTaskStatus.Returned or ChecklistTaskStatus.NotApplicable);
        request.Status = anyProgress ? ChecklistStatus.InProgress : ChecklistStatus.Submitted;
    }

    /// <summary>
    /// An item's attachment, for the task's assignee, the request's final approver, and holders of
    /// the kind's manage permission.
    /// </summary>
    public async Task<ChecklistAttachmentFile?> GetItemAttachmentAsync(int itemId, CancellationToken ct = default)
    {
        var item = await _db.ChecklistTaskItems.AsNoTracking()
            .Where(i => i.Id == itemId && i.AttachmentFileName != null)
            .Select(i => new
            {
                i.AttachmentFileName,
                i.AttachmentContentType,
                i.ChecklistTask!.AssignedUserId,
                Request = i.ChecklistTask.ChecklistRequest
            })
            .FirstOrDefaultAsync(ct);
        if (item?.Request is null) return null;
        if (!CanHandleTask(item.AssignedUserId) && !IsFinalApprover(item.Request) && !CanManage(item.Request.Kind)) return null;

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

    /// <summary>Only the assignee; administrators deliberately get no override.</summary>
    private bool CanHandleTask(string? assignedUserId)
        => !string.IsNullOrWhiteSpace(_currentUser.UserId) && _currentUser.UserId == assignedUserId;

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
            $"<p>All departments have completed the departure checklist for <b>{employee?.Name}</b> ({employee?.EmployeeNo}).</p>" +
            "<p>Please review and approve it in HR System under My Checklist Tasks.</p>"));
    }

    private async Task NotifyReturnAsync(ChecklistTask task, CancellationToken ct)
    {
        if (task.AssignedUserId is null) return;
        var assignee = await _users.GetByIdAsync(task.AssignedUserId, ct);
        if (string.IsNullOrWhiteSpace(assignee?.Email)) return;

        var request = task.ChecklistRequest!;
        _email.Enqueue(new EmailMessage(
            new[] { assignee.Email },
            $"[Returned] Departure checklist ({request.RequestNo}) - {task.DepartmentName}",
            $"<p>The final reviewer returned the {System.Net.WebUtility.HtmlEncode(task.DepartmentName)} task for " +
            $"<b>{request.Employee?.Name}</b> ({request.Employee?.EmployeeNo}).</p>" +
            $"<p>Reason: {System.Net.WebUtility.HtmlEncode(task.ReturnReason)}</p><p>Please update it under My Checklist Tasks.</p>"));
    }

    private async Task NotifySubmitterOfApprovalAsync(ChecklistRequest request, CancellationToken ct)
    {
        if (request.SubmittedBy is null) return;
        var submitter = await _users.GetByIdAsync(request.SubmittedBy, ct);
        if (string.IsNullOrWhiteSpace(submitter?.Email)) return;

        _email.Enqueue(new EmailMessage(
            new[] { submitter.Email },
            $"[Completed] Departure ({request.RequestNo})",
            $"<p>{System.Net.WebUtility.HtmlEncode(request.FinalApproverUserName)} approved the departure of " +
            $"<b>{request.Employee?.Name}</b> ({request.Employee?.EmployeeNo}). The employee is now marked as resigned.</p>"));
    }

    private async Task NotifyCancellationAsync(ChecklistRequest request, CancellationToken ct)
    {
        var label = Label(request.Kind);
        var openAssignees = request.Tasks
            .Where(t => t.AssignedUserId is not null && t.Status is not (ChecklistTaskStatus.Completed or ChecklistTaskStatus.NotApplicable))
            .Select(t => t.AssignedUserId!)
            .Distinct();
        var emails = (await _users.GetByIdsAsync(openAssignees, ct)).Values
            .Select(u => u.Email)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!)
            .ToList();
        if (emails.Count == 0) return;

        _email.Enqueue(new EmailMessage(
            emails,
            $"[Cancelled] {char.ToUpper(label[0]) + label[1..]} checklist ({request.RequestNo})",
            $"<p>The {label} checklist for <b>{request.Employee?.Name}</b> ({request.Employee?.EmployeeNo}) was cancelled. No further action is needed.</p>" +
            $"<p>Reason: {System.Net.WebUtility.HtmlEncode(request.CancelReason)}</p>"));
    }

    private void NotifyAssignees(ChecklistRequest request, IReadOnlyList<ChecklistTask> tasks, IReadOnlyDictionary<string, UserDirectoryEntry> assignees)
    {
        var label = Label(request.Kind);
        foreach (var group in tasks.GroupBy(t => t.AssignedUserId!))
        {
            var departments = string.Join(", ", group.Select(x => x.DepartmentName));
            _email.Enqueue(new EmailMessage(
                new[] { assignees[group.Key].Email! },
                $"[Action Required] {char.ToUpper(label[0]) + label[1..]} checklist ({request.RequestNo})",
                $"<p>You have new {label} checklist task(s) for <b>{request.Employee?.Name}</b> ({request.Employee?.EmployeeNo}).</p>" +
                $"<p>Departments: {departments}</p><p>Please process them in HR System under My Checklist Tasks.</p>"));
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
        Tasks = request.Tasks.OrderBy(t => t.SortOrder).Select(MapTask).ToList()
    };

    private static ChecklistTaskDto MapTask(ChecklistTask task) => new()
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
