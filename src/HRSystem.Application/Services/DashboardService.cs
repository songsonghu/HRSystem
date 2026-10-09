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

    public DashboardService(IAppDbContext db, ICurrentUser currentUser, IUserDirectoryService users)
    {
        _db = db;
        _currentUser = currentUser;
        _users = users;
    }

    public async Task<DashboardDto> GetAsync(CancellationToken ct = default)
    {
        var userId = _currentUser.UserId ?? string.Empty;
        var employee = await _db.Employees.AsNoTracking()
            .Where(e => e.UserId == userId)
            .Select(e => new { e.Id, e.Name })
            .FirstOrDefaultAsync(ct);

        var employeeId = employee?.Id ?? -1; // -1 matches nothing for logins without an employee record

        var dto = new DashboardDto
        {
            DisplayName = employee?.Name ?? (await _users.GetByIdAsync(userId, ct))?.UserName ?? _currentUser.UserName ?? string.Empty,
            MyAccountTasks = await _db.AccountRequestItems.CountAsync(i =>
                i.AssignedUserId == userId
                && i.Status != ItemStatus.Completed && i.Status != ItemStatus.Rejected
                && (i.Request!.Status == RequestStatus.Submitted || i.Request.Status == RequestStatus.InProgress), ct),
            MyChecklistTasks = await _db.ChecklistTasks.CountAsync(t =>
                t.AssignedUserId == userId
                && t.Status != ChecklistTaskStatus.Completed && t.Status != ChecklistTaskStatus.NotApplicable
                && t.ChecklistRequest!.Status != ChecklistStatus.Draft
                && t.ChecklistRequest.Status != ChecklistStatus.Completed
                && t.ChecklistRequest.Status != ChecklistStatus.Cancelled, ct),
            WaitingForMyApproval = await _db.AccountRequests.CountAsync(r =>
                r.Status == RequestStatus.PendingApproval && r.ApproverUserId == userId, ct),
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
