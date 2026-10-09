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
/// department, assigned to its manager) -> departments complete checklists in parallel
/// -> pending final review -> finalize.
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

        return Result<ChecklistCreatePageDto>.Success(new ChecklistCreatePageDto
        {
            Kind = kind,
            EmployeeId = employee!.Id,
            EmployeeNo = employee.EmployeeNo,
            EmployeeName = employee.Name,
            Department = employee.Department?.Name,
            Position = employee.Position,
            JoinDate = employee.JoinDate,
            Templates = await GetTemplatePreviewAsync(kind, ct),
            AccountTypeOptions = kind == ChecklistKind.Onboarding
                ? await _accountRequests.GetAccountTypeOptionsAsync(employee.Id, ct)
                : Array.Empty<AccountTypeOptionDto>()
        });
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

        // Onboarding: the accounts to open travel as a linked Onboard account request.
        if (dto.Kind == ChecklistKind.Onboarding && dto.AccountTypeIds.Count > 0)
        {
            var accountRequest = await _accountRequests.CreateAsync(new CreateRequestDto
            {
                EmployeeId = dto.EmployeeId,
                RequestType = RequestType.Onboard,
                AccountTypeIds = dto.AccountTypeIds,
                Details = dto.AccountDetails,
                IsNewHeadcount = true,
                Remark = $"Raised with onboarding {request.RequestNo}."
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
        return await _db.ChecklistTemplates.AsNoTracking()
            .Where(t => t.Kind == kind && t.IsActive && !t.IsDeleted)
            .OrderBy(t => t.SortOrder)
            .Select(t => new ChecklistTemplatePreviewDto
            {
                DepartmentName = t.Department!.Name,
                ItemCount = t.Items.Count,
                CanReceiveTasks = t.Department.Manager != null && t.Department.Manager.UserId != null
            })
            .ToListAsync(ct);
    }

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
        var unrouted = templates.Where(t => t.Department?.Manager?.UserId is null).Select(t => t.Department!.Name).ToList();
        if (unrouted.Count > 0)
            return Result.Fail($"No manager with a login account is set for: {string.Join(", ", unrouted)}. Set one under Departments first.");

        var assignees = new Dictionary<string, UserDirectoryEntry>();
        foreach (var userId in templates.Select(t => t.Department!.Manager!.UserId!).Distinct())
        {
            var user = await _users.GetByIdAsync(userId, ct);
            if (string.IsNullOrWhiteSpace(user?.Email))
                return Result.Fail($"The manager login of {templates.First(t => t.Department!.Manager!.UserId == userId).Department!.Name} has no email.");
            assignees[userId] = user;
        }

        // Onboarding: submit the linked account request first; if its routing fails nothing is dispatched.
        if (snapshot.AccountRequest is { Status: RequestStatus.Draft })
        {
            var submitted = await _accountRequests.SubmitAsync(snapshot.AccountRequest.Id, ct);
            if (!submitted.Succeeded) return Result.Fail($"Account request {snapshot.AccountRequest.RequestNo}: {submitted.Error}");
        }

        var request = await _db.ChecklistRequests.Include(r => r.Employee).FirstAsync(r => r.Id == requestId, ct);
        var tasks = templates.Select(template =>
        {
            var assignee = assignees[template.Department!.Manager!.UserId!];
            var task = new ChecklistTask
            {
                ChecklistRequestId = request.Id,
                AssignedDepartmentId = template.DepartmentId,
                DepartmentName = template.Department.Name,
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
                    CreatedBy = _currentUser.UserId
                });
            }
            return task;
        }).ToList();

        _db.ChecklistTasks.AddRange(tasks);
        request.Status = ChecklistStatus.Submitted;
        request.SubmittedAt = DateTime.UtcNow;
        request.SubmittedBy = _currentUser.UserId;
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
        if (request is null || !CanManage(request.Kind)) return Result.Fail("Request not found.");
        if (request.Status == ChecklistStatus.Completed) return Result.Fail("This request is already finalized.");

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
        await _audit.LogAsync($"Finalize{request.Kind}", nameof(ChecklistRequest), request.Id.ToString(), request.RequestNo, ct);
        return Result.Success();
    }

    // ------------------------------------------------------------------ tasks

    public async Task<IReadOnlyList<ChecklistTaskDto>> GetMyPendingTasksAsync(CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId)) return Array.Empty<ChecklistTaskDto>();

        var query = _db.ChecklistTasks.AsNoTracking()
            .Include(t => t.ChecklistRequest)!.ThenInclude(r => r!.Employee)
            .Include(t => t.Items)
            .Where(t => t.ChecklistRequest!.Status != ChecklistStatus.Draft
                        && t.ChecklistRequest.Status != ChecklistStatus.Completed
                        && t.ChecklistRequest.Status != ChecklistStatus.Cancelled
                        && t.Status != ChecklistTaskStatus.Completed
                        && t.Status != ChecklistTaskStatus.NotApplicable);

        if (!_currentUser.IsAdmin)
            query = query.Where(t => t.AssignedUserId == userId);

        var tasks = await query.OrderBy(t => t.ChecklistRequestId).ThenBy(t => t.SortOrder).ToListAsync(ct);
        return tasks.Select(MapTask).ToList();
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

        var now = DateTime.UtcNow;
        foreach (var updateItem in dto.Items)
        {
            var item = task.Items.FirstOrDefault(i => i.Id == updateItem.Id);
            if (item is null) continue;

            item.IsNotApplicable = updateItem.IsNotApplicable;
            item.IsCompleted = updateItem.IsCompleted && !updateItem.IsNotApplicable;
            item.Remark = updateItem.Remark;
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

        var hasProgress = task.Items.Any(i => i.IsCompleted || i.IsNotApplicable || !string.IsNullOrWhiteSpace(i.Remark));
        var allRequiredDone = task.Items.Where(i => i.IsRequired).All(i => i.IsCompleted || i.IsNotApplicable);

        if (dto.MarkAsCompleted)
        {
            if (!allRequiredDone)
                return Result.Fail("Complete all required checklist items before submitting the department task.");

            var requiredItems = task.Items.Where(i => i.IsRequired).ToList();
            task.Status = requiredItems.Count > 0 && requiredItems.All(i => i.IsNotApplicable)
                ? ChecklistTaskStatus.NotApplicable
                : ChecklistTaskStatus.Completed;
            task.CompletedAt = now;
        }
        else
        {
            task.Status = hasProgress ? ChecklistTaskStatus.InProgress : ChecklistTaskStatus.NotStarted;
            task.CompletedAt = null;
        }

        // Always write the request row so its row version detects a parallel update by another
        // department; the retry then recomputes from the tasks that department just changed.
        await RecomputeRequestStatusAsync(task.ChecklistRequest, ct);
        task.ChecklistRequest.ModifiedAt = now;
        await _db.SaveChangesAsync(ct);
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

    private bool CanHandleTask(string? assignedUserId)
        => _currentUser.IsAdmin || (!string.IsNullOrWhiteSpace(_currentUser.UserId) && _currentUser.UserId == assignedUserId);

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
        Items = task.Items.OrderBy(i => i.SortOrder).Select(i => new ChecklistTaskItemDto
        {
            Id = i.Id,
            Description = i.Description,
            SortOrder = i.SortOrder,
            IsRequired = i.IsRequired,
            IsCompleted = i.IsCompleted,
            IsNotApplicable = i.IsNotApplicable,
            Remark = i.Remark
        }).ToList()
    };
}
