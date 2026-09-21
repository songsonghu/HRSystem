using HRSystem.Application.Common;
using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Domain.Entities;
using HRSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Services;

public class DepartureService : IDepartureService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IEmailService _email;
    private readonly IUserDirectoryService _users;

    public DepartureService(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditService audit,
        IEmailService email,
        IUserDirectoryService users)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _email = email;
        _users = users;
    }

    public async Task<Result<DepartureCreatePageDto>> GetCreatePageAsync(int employeeId, CancellationToken ct = default)
    {
        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId && !e.IsDeleted, ct);
        if (employee is null || employee.Status != EmployeeStatus.Active)
            return Result<DepartureCreatePageDto>.Fail("Only active employees can start departure.");

        var hasOpen = await _db.DepartureRequests.AnyAsync(r =>
            r.EmployeeId == employeeId
            && r.Status != DepartureRequestStatus.Completed
            && r.Status != DepartureRequestStatus.Cancelled, ct);
        if (hasOpen)
            return Result<DepartureCreatePageDto>.Fail("This employee already has an open departure request.");

        var templates = await _db.DepartureTaskTemplates.AsNoTracking()
            .Include(t => t.Items)
            .Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(ct);

        return Result<DepartureCreatePageDto>.Success(new DepartureCreatePageDto
        {
            EmployeeId = employee.Id,
            EmployeeNo = employee.EmployeeNo,
            EmployeeName = employee.Name,
            Department = employee.Department,
            Position = employee.Position,
            JoinDate = employee.JoinDate,
            LastWorkingDateDefault = DateTime.Today,
            LastEmploymentDateDefault = DateTime.Today,
            TemplateDepartments = templates
                .Select(t => new DepartureTemplatePreviewDto { DepartmentName = t.DepartmentName, ItemCount = t.Items.Count })
                .ToList()
        });
    }

    public async Task<Result<int>> CreateDraftAsync(CreateDepartureRequestDto dto, CancellationToken ct = default)
    {
        if (dto.LastEmploymentDate < dto.LastWorkingDate)
            return Result<int>.Fail("Last employment date must be on or after last working date.");

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == dto.EmployeeId && !e.IsDeleted, ct);
        if (employee is null || employee.Status != EmployeeStatus.Active)
            return Result<int>.Fail("Only active employees can start departure.");

        var hasOpen = await _db.DepartureRequests.AnyAsync(r =>
            r.EmployeeId == dto.EmployeeId
            && r.Status != DepartureRequestStatus.Completed
            && r.Status != DepartureRequestStatus.Cancelled, ct);
        if (hasOpen)
            return Result<int>.Fail("This employee already has an open departure request.");

        var request = new DepartureRequest
        {
            RequestNo = $"TMP-{Guid.NewGuid():N}"[..16],
            EmployeeId = dto.EmployeeId,
            Status = DepartureRequestStatus.Draft,
            LastWorkingDate = dto.LastWorkingDate,
            LastEmploymentDate = dto.LastEmploymentDate,
            Reason = dto.Reason,
            Remark = dto.Remark,
            CreatedBy = _currentUser.UserId
        };

        _db.DepartureRequests.Add(request);
        await _db.SaveChangesAsync(ct);

        request.RequestNo = $"DEP-{DateTime.UtcNow.Year}-{request.Id:D5}";
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("CreateDepartureDraft", nameof(DepartureRequest), request.Id.ToString(), request.RequestNo, ct);
        return Result<int>.Success(request.Id);
    }

    public async Task<DepartureRequestDto?> GetAsync(int requestId, CancellationToken ct = default)
    {
        var request = await _db.DepartureRequests.AsNoTracking()
            .Include(r => r.Employee)
            .Include(r => r.Tasks.OrderBy(t => t.SortOrder))
            .ThenInclude(t => t.Items.OrderBy(i => i.SortOrder))
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);

        return request is null ? null : MapRequest(request);
    }

    public async Task<Result> SubmitAsync(int requestId, CancellationToken ct = default)
    {
        var request = await _db.DepartureRequests
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return Result.Fail("Departure request not found.");
        if (request.Status != DepartureRequestStatus.Draft) return Result.Fail("Only draft requests can be submitted.");

        var templates = await _db.DepartureTaskTemplates
            .Include(t => t.Items)
            .Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(ct);
        if (templates.Count == 0) return Result.Fail("No active departure templates found.");

        var departments = await _db.Departments.ToListAsync(ct);
        var tasks = new List<DepartureTask>();

        foreach (var template in templates)
        {
            var department = departments.FirstOrDefault(d => d.Name == template.DepartmentName);
            if (department is null)
                return Result.Fail($"Department '{template.DepartmentName}' is not configured.");
            if (string.IsNullOrWhiteSpace(department.HeadUserId))
                return Result.Fail($"Department '{template.DepartmentName}' has no assigned department head.");

            var user = await _users.GetByIdAsync(department.HeadUserId, ct);
            if (string.IsNullOrWhiteSpace(user?.Email))
                return Result.Fail($"Department '{template.DepartmentName}' head user email is missing.");

            var task = new DepartureTask
            {
                DepartureRequestId = request.Id,
                AssignedDepartmentId = department.Id,
                DepartmentName = template.DepartmentName,
                AssignedUserId = user.UserId,
                AssignedUserName = user.UserName,
                Status = DepartureTaskStatus.NotStarted,
                IsRequired = template.IsRequired,
                SortOrder = template.SortOrder,
                CreatedBy = _currentUser.UserId
            };

            foreach (var item in template.Items.OrderBy(i => i.SortOrder))
            {
                task.Items.Add(new DepartureTaskItem
                {
                    Description = item.Description,
                    SortOrder = item.SortOrder,
                    IsRequired = item.IsRequired,
                    CreatedBy = _currentUser.UserId
                });
            }

            tasks.Add(task);
        }

        _db.DepartureTasks.AddRange(tasks);

        request.Status = DepartureRequestStatus.Submitted;
        request.SubmittedAt = DateTime.UtcNow;
        request.SubmittedBy = _currentUser.UserId;

        await _db.SaveChangesAsync(ct);
        await NotifyDepartmentHeadsAsync(request, tasks, ct);
        await _audit.LogAsync("SubmitDeparture", nameof(DepartureRequest), request.Id.ToString(), request.RequestNo, ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<DepartureTaskDto>> GetMyPendingTasksAsync(CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId)) return Array.Empty<DepartureTaskDto>();

        var query = _db.DepartureTasks.AsNoTracking()
            .Include(t => t.DepartureRequest)!.ThenInclude(r => r!.Employee)
            .Include(t => t.Items)
            .Where(t => t.DepartureRequest!.Status != DepartureRequestStatus.Draft
                        && t.DepartureRequest.Status != DepartureRequestStatus.Completed
                        && t.DepartureRequest.Status != DepartureRequestStatus.Cancelled
                        && t.Status != DepartureTaskStatus.Completed
                        && t.Status != DepartureTaskStatus.NotApplicable);

        if (!_currentUser.IsInRole("Admin"))
            query = query.Where(t => t.AssignedUserId == userId);

        var tasks = await query.OrderBy(t => t.SortOrder).ToListAsync(ct);
        return tasks.Select(MapTask).ToList();
    }

    public async Task<DepartureTaskDto?> GetTaskAsync(int taskId, CancellationToken ct = default)
    {
        var task = await _db.DepartureTasks.AsNoTracking()
            .Include(t => t.DepartureRequest)!.ThenInclude(r => r!.Employee)
            .Include(t => t.Items.OrderBy(i => i.SortOrder))
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null) return null;

        if (!CanHandleTask(task.AssignedUserId)) return null;
        return MapTask(task);
    }

    public async Task<Result> UpdateTaskAsync(DepartureTaskUpdateDto dto, CancellationToken ct = default)
    {
        var task = await _db.DepartureTasks
            .Include(t => t.DepartureRequest)
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == dto.TaskId, ct);
        if (task is null) return Result.Fail("Departure task not found.");
        if (!CanHandleTask(task.AssignedUserId)) return Result.Fail("You are not allowed to update this task.");
        if (task.DepartureRequest is null || task.DepartureRequest.Status == DepartureRequestStatus.Completed)
            return Result.Fail("Request already completed.");

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
                ? DepartureTaskStatus.NotApplicable
                : DepartureTaskStatus.Completed;
            task.CompletedAt = now;
        }
        else
        {
            task.Status = hasProgress ? DepartureTaskStatus.InProgress : DepartureTaskStatus.NotStarted;
            task.CompletedAt = null;
        }

        await RecomputeRequestStatusAsync(task.DepartureRequest!, ct);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("UpdateDepartureTask", nameof(DepartureTask), task.Id.ToString(), task.Status.ToString(), ct);
        return Result.Success();
    }

    public async Task<Result> FinalizeAsync(int requestId, CancellationToken ct = default)
    {
        var request = await _db.DepartureRequests
            .Include(r => r.Employee)
            .Include(r => r.Tasks)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return Result.Fail("Departure request not found.");
        if (!_currentUser.IsInRole("Admin") && !_currentUser.IsInRole("HR"))
            return Result.Fail("Only Admin/HR can finalize departure requests.");

        var requiredTasks = request.Tasks.Where(t => t.IsRequired).ToList();
        if (requiredTasks.Count == 0)
            return Result.Fail("Departure request has no required department tasks.");

        if (requiredTasks.Any(t => t.Status is not (DepartureTaskStatus.Completed or DepartureTaskStatus.NotApplicable)))
            return Result.Fail("All required department tasks must be completed before finalization.");

        request.Status = DepartureRequestStatus.Completed;
        request.FinalizedAt = DateTime.UtcNow;
        request.FinalizedBy = _currentUser.UserId;
        request.CompletedAt = request.FinalizedAt;

        if (request.Employee is not null)
        {
            request.Employee.Status = EmployeeStatus.Resigned;
            request.Employee.ResignDate = request.LastEmploymentDate;
            request.Employee.ModifiedBy = _currentUser.UserId;
            request.Employee.ModifiedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("FinalizeDeparture", nameof(DepartureRequest), request.Id.ToString(), request.RequestNo, ct);
        return Result.Success();
    }

    private async Task RecomputeRequestStatusAsync(DepartureRequest request, CancellationToken ct)
    {
        var tasks = await _db.DepartureTasks.Where(t => t.DepartureRequestId == request.Id).ToListAsync(ct);
        if (tasks.Count == 0)
        {
            request.Status = DepartureRequestStatus.Submitted;
            return;
        }

        var requiredTasks = tasks.Where(t => t.IsRequired).ToList();
        if (requiredTasks.All(t => t.Status is DepartureTaskStatus.Completed or DepartureTaskStatus.NotApplicable))
        {
            request.Status = DepartureRequestStatus.PendingFinalReview;
            return;
        }

        var anyProgress = tasks.Any(t => t.Status is DepartureTaskStatus.InProgress or DepartureTaskStatus.Completed or DepartureTaskStatus.Returned or DepartureTaskStatus.NotApplicable);
        request.Status = anyProgress ? DepartureRequestStatus.InProgress : DepartureRequestStatus.Submitted;
    }

    private bool CanHandleTask(string? assignedUserId)
        => _currentUser.IsInRole("Admin") || (!string.IsNullOrWhiteSpace(_currentUser.UserId) && _currentUser.UserId == assignedUserId);

    private async Task NotifyDepartmentHeadsAsync(DepartureRequest request, IReadOnlyList<DepartureTask> tasks, CancellationToken ct)
    {
        foreach (var group in tasks.GroupBy(t => t.AssignedUserId).Where(g => !string.IsNullOrWhiteSpace(g.Key)))
        {
            var user = await _users.GetByIdAsync(group.Key!, ct);
            if (string.IsNullOrWhiteSpace(user?.Email)) continue;

            var departments = string.Join(", ", group.Select(x => x.DepartmentName));
            _email.Enqueue(new EmailMessage(
                new[] { user.Email! },
                $"[Action Required] Departure task assigned ({request.RequestNo})",
                $"<p>You have new departure checklist task(s) for <b>{request.Employee?.Name}</b> ({request.Employee?.EmployeeNo}).</p><p>Departments: {departments}</p><p>Please process them in HR System under My Departure Tasks.</p>"));
        }
    }

    private static DepartureRequestDto MapRequest(DepartureRequest request) => new()
    {
        Id = request.Id,
        RequestNo = request.RequestNo,
        EmployeeId = request.EmployeeId,
        EmployeeNo = request.Employee?.EmployeeNo ?? string.Empty,
        EmployeeName = request.Employee?.Name ?? string.Empty,
        Status = request.Status,
        LastWorkingDate = request.LastWorkingDate,
        LastEmploymentDate = request.LastEmploymentDate,
        Reason = request.Reason,
        Remark = request.Remark,
        SubmittedAt = request.SubmittedAt,
        FinalizedAt = request.FinalizedAt,
        CompletedAt = request.CompletedAt,
        Tasks = request.Tasks.OrderBy(t => t.SortOrder).Select(MapTask).ToList()
    };

    private static DepartureTaskDto MapTask(DepartureTask task) => new()
    {
        Id = task.Id,
        DepartureRequestId = task.DepartureRequestId,
        RequestNo = task.DepartureRequest?.RequestNo ?? string.Empty,
        EmployeeNo = task.DepartureRequest?.Employee?.EmployeeNo ?? string.Empty,
        EmployeeName = task.DepartureRequest?.Employee?.Name ?? string.Empty,
        DepartmentName = task.DepartmentName,
        Status = task.Status,
        IsRequired = task.IsRequired,
        SortOrder = task.SortOrder,
        TaskRemark = task.TaskRemark,
        CompletedAt = task.CompletedAt,
        Items = task.Items.OrderBy(i => i.SortOrder).Select(i => new DepartureTaskItemDto
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
