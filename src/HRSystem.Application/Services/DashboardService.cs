using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using HRSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Services;

public class DashboardService : IDashboardService
{
    private static readonly RequestStatus[] OpenRequestStatuses =
        { RequestStatus.Draft, RequestStatus.PendingApproval, RequestStatus.Submitted, RequestStatus.InProgress };

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IUserDirectoryService _users;
    private readonly IAssignmentService _assignments;

    public DashboardService(IAppDbContext db, ICurrentUser currentUser, IUserDirectoryService users, IAssignmentService assignments)
    {
        _db = db;
        _currentUser = currentUser;
        _users = users;
        _assignments = assignments;
    }

    public async Task<DashboardDto> GetAsync(CancellationToken ct = default)
    {
        var userId = _currentUser.UserId ?? string.Empty;
        var employee = await _db.Employees.AsNoTracking()
            .Where(e => e.UserId == userId)
            .Select(e => new { e.Id, e.Name })
            .FirstOrDefaultAsync(ct);

        var employeeId = employee?.Id ?? -1; // -1 matches nothing for logins without an employee record

        var accountHandles = await _assignments.GetDepartmentIdsAsync(userId, WorkArea.Account, AssignmentRole.Handler, ct);
        var accountApproves = await _assignments.GetDepartmentIdsAsync(userId, WorkArea.Account, AssignmentRole.Approver, ct);
        var departureHandles = await _assignments.GetDepartmentIdsAsync(userId, WorkArea.Departure, AssignmentRole.Handler, ct);
        var departureApproves = await _assignments.GetDepartmentIdsAsync(userId, WorkArea.Departure, AssignmentRole.Approver, ct);

        var openItems = _db.AccountRequestItems.Where(i =>
            i.Request!.Status == RequestStatus.Submitted || i.Request.Status == RequestStatus.InProgress);
        var openTasks = _db.ChecklistTasks.Where(t =>
            t.AssignedDepartmentId != null
            && t.ChecklistRequest!.Status != ChecklistStatus.Draft
            && t.ChecklistRequest.Status != ChecklistStatus.Completed
            && t.ChecklistRequest.Status != ChecklistStatus.Cancelled);

        var dto = new DashboardDto
        {
            DisplayName = employee?.Name ?? (await _users.GetByIdAsync(userId, ct))?.UserName ?? _currentUser.UserName ?? string.Empty,
            AccountChecklist = await openItems.CountAsync(i =>
                accountHandles.Contains(i.AssignedDeptId) && i.Status != ItemStatus.Completed && i.Status != ItemStatus.Rejected, ct),
            AccountApproval = await openItems.CountAsync(i =>
                    accountApproves.Contains(i.AssignedDeptId) && i.ApprovedAt == null
                    && (i.Status == ItemStatus.Completed || i.Status == ItemStatus.Rejected), ct)
                + await _db.AccountRequests.CountAsync(r => r.Status == RequestStatus.PendingApproval && r.ApproverUserId == userId, ct),
            DepartureChecklist = await openTasks.CountAsync(t =>
                departureHandles.Contains(t.AssignedDepartmentId!.Value)
                && t.Status != ChecklistTaskStatus.Completed && t.Status != ChecklistTaskStatus.NotApplicable, ct),
            DepartureApproval = await openTasks.CountAsync(t =>
                    departureApproves.Contains(t.AssignedDepartmentId!.Value) && t.ApprovedAt == null
                    && (t.Status == ChecklistTaskStatus.Completed || t.Status == ChecklistTaskStatus.NotApplicable), ct)
                + await _db.ChecklistRequests.CountAsync(r =>
                    r.Kind == ChecklistKind.Departure && r.FinalApproverUserId == userId
                    && r.Status == ChecklistStatus.PendingFinalReview, ct),
            MyOpenRequests = await _db.AccountRequests.CountAsync(r =>
                OpenRequestStatuses.Contains(r.Status)
                && (r.CreatedBy == userId || r.EmployeeId == employeeId), ct)
        };

        if (_currentUser.HasPermission(Permissions.EmployeesManage))
            dto.ActiveEmployees = await _db.Employees.CountAsync(e => e.Status == EmployeeStatus.Active, ct);
        if (_currentUser.HasPermission(Permissions.OnboardingManage))
            dto.OpenOnboarding = await CountOpenChecklistsAsync(ChecklistKind.Onboarding, ct);
        if (_currentUser.HasPermission(Permissions.DeparturesManage))
            dto.OpenDepartures = await CountOpenChecklistsAsync(ChecklistKind.Departure, ct);

        var recent = _db.AccountRequests.AsNoTracking();
        if (_currentUser.HasPermission(Permissions.AccountRequestsManage))
            dto.OpenAccountRequests = await _db.AccountRequests.CountAsync(r => OpenRequestStatuses.Contains(r.Status), ct);
        else
            recent = recent.Where(r => r.CreatedBy == userId || r.EmployeeId == employeeId || r.ApproverUserId == userId);

        dto.RecentRequests = await recent
            .OrderByDescending(r => r.Id)
            .Take(6)
            .Select(r => new DashboardRequestDto(r.Id, r.RequestNo, r.Employee!.Name, r.RequestType, r.Status, r.CreatedAt))
            .ToListAsync(ct);

        return dto;
    }

    private Task<int> CountOpenChecklistsAsync(ChecklistKind kind, CancellationToken ct)
        => _db.ChecklistRequests.CountAsync(r =>
            r.Kind == kind && r.Status != ChecklistStatus.Completed && r.Status != ChecklistStatus.Cancelled, ct);
}
