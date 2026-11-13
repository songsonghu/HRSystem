using HRSystem.Application.Common;
using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using HRSystem.Domain.Entities;
using HRSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Services;

/// <summary>
/// Resolves the handlers and approvers of each department. Work is shown to whoever holds the role
/// at the time, so changing the settings moves unfinished work at once.
/// </summary>
public class AssignmentService : IAssignmentService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IUserDirectoryService _users;

    public AssignmentService(IAppDbContext db, ICurrentUser currentUser, IAuditService audit, IUserDirectoryService users)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _users = users;
    }

    public static string RoleLabel(AssignmentRole role) => role == AssignmentRole.Handler ? "handler" : "approver";

    /// <summary>The users for display, or null when there are none.</summary>
    public static string? Names(IReadOnlyList<UserDirectoryEntry> users)
        => users.Count == 0 ? null : string.Join(", ", users.Select(u => u.Display));

    public async Task<IReadOnlyList<int>> GetDepartmentIdsAsync(string? userId, WorkArea area, AssignmentRole role, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Array.Empty<int>();

        var configured = await _db.DepartmentAssignees.AsNoTracking()
            .Where(a => a.Area == area && a.Role == role)
            .Select(a => new { a.DepartmentId, a.UserId })
            .ToListAsync(ct);
        var staffed = configured.Select(a => a.DepartmentId).ToHashSet();
        var managed = await _db.Departments.AsNoTracking()
            .Where(d => d.Manager != null && d.Manager.UserId == userId)
            .Select(d => d.Id)
            .ToListAsync(ct);

        return configured.Where(a => a.UserId == userId).Select(a => a.DepartmentId)
            .Concat(managed.Where(id => !staffed.Contains(id)))
            .Distinct()
            .ToList();
    }

    public async Task<bool> HoldsAsync(string? userId, int? departmentId, WorkArea area, AssignmentRole role, CancellationToken ct = default)
        => departmentId is int id && (await GetDepartmentIdsAsync(userId, area, role, ct)).Contains(id);

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<UserDirectoryEntry>>> GetAssigneesAsync(
        IEnumerable<int> departmentIds, WorkArea area, AssignmentRole role, CancellationToken ct = default)
    {
        var ids = departmentIds.Distinct().ToList();
        var configured = await _db.DepartmentAssignees.AsNoTracking()
            .Where(a => a.Area == area && a.Role == role && ids.Contains(a.DepartmentId))
            .Select(a => new { a.DepartmentId, a.UserId })
            .ToListAsync(ct);
        var managers = await _db.Departments.AsNoTracking()
            .Where(d => ids.Contains(d.Id) && d.Manager != null && d.Manager.UserId != null)
            .Select(d => new { d.Id, d.Manager!.UserId })
            .ToListAsync(ct);

        var userIds = new Dictionary<int, List<string>>();
        foreach (var id in ids)
        {
            var list = configured.Where(a => a.DepartmentId == id).Select(a => a.UserId).ToList();
            if (list.Count == 0) list.AddRange(managers.Where(m => m.Id == id).Select(m => m.UserId!));
            userIds[id] = list;
        }

        var users = await _users.GetByIdsAsync(userIds.Values.SelectMany(v => v), ct);
        return userIds.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<UserDirectoryEntry>)kv.Value.Where(users.ContainsKey).Select(u => users[u]).ToList());
    }

    public async Task<string?> CheckCoverageAsync(IEnumerable<int> departmentIds, WorkArea area, CancellationToken ct = default)
    {
        var ids = departmentIds.Distinct().ToList();
        var handlers = await GetAssigneesAsync(ids, area, AssignmentRole.Handler, ct);
        var approvers = await GetAssigneesAsync(ids, area, AssignmentRole.Approver, ct);
        var uncovered = ids.Where(id => handlers[id].Count == 0 || approvers[id].Count == 0).ToList();
        if (uncovered.Count == 0) return null;

        var names = await _db.Departments.AsNoTracking().Where(d => uncovered.Contains(d.Id)).Select(d => d.Name).ToListAsync(ct);
        return $"No handler or approver is set for: {string.Join(", ", names.OrderBy(n => n))}. "
               + "Set them on Settings > Templates for Accounts / Templates for Departure, or give the department a manager with a login.";
    }

    // ---------------------------------------------------------------- settings

    private bool CanManage => _currentUser.HasPermission(Permissions.ChecklistApproversManage);

    /// <summary>Whoever edits the area's templates may see who is set; only checklist-approvers.manage may change it.</summary>
    private bool CanView(WorkArea area)
        => CanManage || _currentUser.HasPermission(area == WorkArea.Account ? Permissions.AccountRequestsManage : Permissions.DeparturesManage);

    public async Task<DepartmentAssignmentDto?> GetDepartmentAsync(int departmentId, WorkArea area, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(area) || !CanView(area)) return null;

        var department = await _db.Departments.AsNoTracking()
            .Where(d => d.Id == departmentId)
            .Select(d => new { d.Id, d.Name, ManagerUserId = d.Manager != null ? d.Manager.UserId : null })
            .FirstOrDefaultAsync(ct);
        if (department is null) return null;

        var configured = await _db.DepartmentAssignees.AsNoTracking()
            .Where(a => a.Area == area && a.DepartmentId == departmentId)
            .ToListAsync(ct);
        var manager = department.ManagerUserId is null ? null : await _users.GetByIdAsync(department.ManagerUserId, ct);
        return new DepartmentAssignmentDto
        {
            DepartmentId = department.Id,
            DepartmentName = department.Name,
            ManagerUserName = manager?.Display,
            HandlerUserIds = configured.Where(a => a.Role == AssignmentRole.Handler).Select(a => a.UserId).ToList(),
            ApproverUserIds = configured.Where(a => a.Role == AssignmentRole.Approver).Select(a => a.UserId).ToList()
        };
    }

    public async Task<Result> SaveSettingsAsync(WorkArea area, IReadOnlyList<DepartmentAssignmentDto> rows, CancellationToken ct = default)
    {
        if (!CanManage || !Enum.IsDefined(area)) return Result.Fail("You do not have permission to manage checklist approvers.");

        var wanted = rows.ToDictionary(
            r => r.DepartmentId,
            r => (Handlers: Clean(r.HandlerUserIds), Approvers: Clean(r.ApproverUserIds)));
        var users = await _users.GetByIdsAsync(wanted.Values.SelectMany(w => w.Handlers.Concat(w.Approvers)), ct);
        if (wanted.Values.SelectMany(w => w.Handlers.Concat(w.Approvers)).Any(id => !users.ContainsKey(id)))
            return Result.Fail("A selected account no longer exists.");

        var departmentIds = wanted.Keys.ToList();
        var known = await _db.Departments.Where(d => departmentIds.Contains(d.Id)).Select(d => new { d.Id, d.Name }).ToListAsync(ct);
        var existing = await _db.DepartmentAssignees.Where(a => a.Area == area && departmentIds.Contains(a.DepartmentId)).ToListAsync(ct);

        var changes = new List<string>();
        foreach (var department in known)
        {
            var (handlers, approvers) = wanted[department.Id];
            foreach (var (role, userIds) in new[] { (AssignmentRole.Handler, handlers), (AssignmentRole.Approver, approvers) })
            {
                var current = existing.Where(a => a.DepartmentId == department.Id && a.Role == role).ToList();
                if (current.Select(a => a.UserId).ToHashSet().SetEquals(userIds)) continue;

                foreach (var removed in current.Where(a => !userIds.Contains(a.UserId)))
                    _db.DepartmentAssignees.Remove(removed);
                foreach (var added in userIds.Where(id => current.All(a => a.UserId != id)))
                    _db.DepartmentAssignees.Add(new DepartmentAssignee { DepartmentId = department.Id, Area = area, Role = role, UserId = added });
                changes.Add($"{department.Name} {RoleLabel(role)}s={(userIds.Count == 0 ? "(manager)" : string.Join("; ", userIds.Select(id => users[id].UserName)))}");
            }
        }

        if (changes.Count == 0) return Result.Success();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("SetDepartmentAssignees", nameof(DepartmentAssignee), area.ToString(), string.Join(" | ", changes), ct);
        return Result.Success();
    }

    private static List<string> Clean(IEnumerable<string>? userIds)
        => (userIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct().ToList();
}
