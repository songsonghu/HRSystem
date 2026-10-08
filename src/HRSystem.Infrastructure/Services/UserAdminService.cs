using HRSystem.Application.Common;
using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Domain.Enums;
using HRSystem.Infrastructure.Identity;
using HRSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Infrastructure.Services;

/// <summary>User management over ASP.NET Core Identity.</summary>
public class UserAdminService : IUserAdminService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;

    public UserAdminService(
        UserManager<ApplicationUser> users,
        RoleManager<IdentityRole> roles,
        AppDbContext db,
        ICurrentUser currentUser,
        IAuditService audit)
    {
        _users = users;
        _roles = roles;
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<IReadOnlyList<UserListItemDto>> GetListAsync(string? keyword = null, CancellationToken ct = default)
    {
        var query = _users.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            query = query.Where(u => u.Email!.Contains(k) || (u.FullName != null && u.FullName.Contains(k)));
        }
        var users = await query.OrderBy(u => u.Email).ToListAsync(ct);

        var roleNames = (await (from ur in _db.UserRoles
                                join r in _db.Roles on ur.RoleId equals r.Id
                                select new { ur.UserId, r.Name }).ToListAsync(ct))
            .ToLookup(x => x.UserId, x => x.Name!);

        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.UserId != null)
            .Select(e => new { e.UserId, e.EmployeeNo, e.Name })
            .ToDictionaryAsync(e => e.UserId!, ct);

        return users.Select(u => new UserListItemDto
        {
            Id = u.Id,
            Email = u.Email ?? u.UserName ?? string.Empty,
            FullName = u.FullName,
            Roles = roleNames[u.Id].OrderBy(n => n).ToList(),
            EmployeeDisplay = employees.TryGetValue(u.Id, out var e) ? $"{e.Name} ({e.EmployeeNo})" : null,
            IsActive = !IsDisabled(u)
        }).ToList();
    }

    public async Task<UserEditDto?> GetAsync(string id, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null) return null;

        return new UserEditDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            Roles = (await _users.GetRolesAsync(user)).ToList(),
            EmployeeId = await _db.Employees.Where(e => e.UserId == id).Select(e => (int?)e.Id).FirstOrDefaultAsync(ct),
            IsActive = !IsDisabled(user)
        };
    }

    public async Task<UserFormOptionsDto> GetFormOptionsAsync(string? userId, CancellationToken ct = default)
    {
        return new UserFormOptionsDto
        {
            Roles = await _roles.Roles.OrderBy(r => r.Name).Select(r => r.Name!).ToListAsync(ct),
            Employees = await _db.Employees.AsNoTracking()
                .Where(e => e.UserId == null || e.UserId == userId)
                .OrderBy(e => e.Name)
                .Select(e => new EmployeeOptionDto(e.Id, e.Name + " (" + e.EmployeeNo + ")"))
                .ToListAsync(ct)
        };
    }

    public async Task<Result<string>> CreateAsync(UserEditDto dto, CancellationToken ct = default)
    {
        var email = dto.Email.Trim();
        if (await _users.FindByEmailAsync(email) is not null)
            return Result<string>.Fail("A user with this email already exists.");

        var roleError = await ValidateRolesAsync(dto.Roles, ct);
        if (roleError is not null) return Result<string>.Fail(roleError);

        return await _db.InTransactionAsync(async () =>
        {
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = dto.FullName?.Trim(),
                LockoutEnabled = true
            };

            var created = await _users.CreateAsync(user, dto.Password!);
            if (!created.Succeeded) return Result<string>.Fail(Describe(created));

            if (dto.Roles.Count > 0)
            {
                var added = await _users.AddToRolesAsync(user, dto.Roles.Distinct());
                if (!added.Succeeded) return Result<string>.Fail(Describe(added));
            }

            var linkError = await LinkEmployeeAsync(user.Id, dto.EmployeeId, ct);
            if (linkError is not null) return Result<string>.Fail(linkError);

            if (!dto.IsActive) await DisableAsync(user);

            await _audit.LogAsync("CreateUser", "User", user.Id, email, ct);
            return Result<string>.Success(user.Id);
        }, ct);
    }

    public async Task<Result> UpdateAsync(UserEditDto dto, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(dto.Id ?? string.Empty);
        if (user is null) return Result.Fail("User not found.");

        var roleError = await ValidateRolesAsync(dto.Roles, ct);
        if (roleError is not null) return Result.Fail(roleError);

        var currentRoles = await _users.GetRolesAsync(user);
        bool isSelf = user.Id == _currentUser.UserId;
        bool wasActiveAdmin = currentRoles.Contains(Roles.Admin) && !IsDisabled(user);
        bool staysActiveAdmin = dto.Roles.Contains(Roles.Admin) && dto.IsActive;

        if (isSelf && !dto.IsActive)
            return Result.Fail("You cannot disable your own account.");
        if (isSelf && currentRoles.Contains(Roles.Admin) && !dto.Roles.Contains(Roles.Admin))
            return Result.Fail("You cannot remove the Admin role from your own account.");
        if (wasActiveAdmin && !staysActiveAdmin && await CountActiveAdminsAsync() <= 1)
            return Result.Fail("At least one active Admin user is required.");

        var email = dto.Email.Trim();
        if (!string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var other = await _users.FindByEmailAsync(email);
            if (other is not null && other.Id != user.Id)
                return Result.Fail("A user with this email already exists.");
        }

        return await _db.InTransactionAsync(async () =>
        {
            if (!string.Equals(email, user.Email, StringComparison.Ordinal))
            {
                var r1 = await _users.SetEmailAsync(user, email);
                if (!r1.Succeeded) return Result.Fail(Describe(r1));
                var r2 = await _users.SetUserNameAsync(user, email); // login uses the email as user name
                if (!r2.Succeeded) return Result.Fail(Describe(r2));
            }

            user.FullName = dto.FullName?.Trim();
            var updated = await _users.UpdateAsync(user);
            if (!updated.Succeeded) return Result.Fail(Describe(updated));

            var toRemove = currentRoles.Except(dto.Roles).ToList();
            if (toRemove.Count > 0)
            {
                var removed = await _users.RemoveFromRolesAsync(user, toRemove);
                if (!removed.Succeeded) return Result.Fail(Describe(removed));
            }
            var toAdd = dto.Roles.Distinct().Except(currentRoles).ToList();
            if (toAdd.Count > 0)
            {
                var added = await _users.AddToRolesAsync(user, toAdd);
                if (!added.Succeeded) return Result.Fail(Describe(added));
            }

            var linkError = await LinkEmployeeAsync(user.Id, dto.EmployeeId, ct);
            if (linkError is not null) return Result.Fail(linkError);

            if (dto.IsActive && IsDisabled(user))
                await _users.SetLockoutEndDateAsync(user, null);
            else if (!dto.IsActive && !IsDisabled(user))
                await DisableAsync(user);

            await _audit.LogAsync("UpdateUser", "User", user.Id,
                $"{email}; roles={string.Join(",", dto.Roles)}; active={dto.IsActive}", ct);
            return Result.Success();
        }, ct);
    }

    public async Task<Result> ResetPasswordAsync(ResetPasswordDto dto, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(dto.Id);
        if (user is null) return Result.Fail("User not found.");

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var reset = await _users.ResetPasswordAsync(user, token, dto.NewPassword);
        if (!reset.Succeeded) return Result.Fail(Describe(reset));

        await _audit.LogAsync("ResetUserPassword", "User", user.Id, user.Email, ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(string id, CancellationToken ct = default)
    {
        if (id == _currentUser.UserId) return Result.Fail("You cannot delete your own account.");

        var user = await _users.FindByIdAsync(id);
        if (user is null) return Result.Fail("User not found.");

        if (await _users.IsInRoleAsync(user, Roles.Admin) && !IsDisabled(user) && await CountActiveAdminsAsync() <= 1)
            return Result.Fail("At least one active Admin user is required.");

        var managerOf = await _db.Departments
            .Where(d => d.Manager != null && d.Manager.UserId == id)
            .Select(d => d.Name)
            .ToListAsync(ct);
        if (managerOf.Count > 0)
            return Result.Fail($"This user's employee record is the manager of: {string.Join(", ", managerOf)}. Assign another manager first, or disable the user instead.");

        bool hasOpenAccountTasks = await _db.AccountRequestItems.AnyAsync(i =>
            i.AssignedUserId == id && i.Status != ItemStatus.Completed && i.Status != ItemStatus.Rejected, ct);
        bool hasOpenDepartureTasks = await _db.DepartureTasks.AnyAsync(t =>
            t.AssignedUserId == id
            && t.Status != DepartureTaskStatus.Completed && t.Status != DepartureTaskStatus.NotApplicable
            && t.DepartureRequest!.Status != DepartureRequestStatus.Completed
            && t.DepartureRequest.Status != DepartureRequestStatus.Cancelled, ct);
        if (hasOpenAccountTasks || hasOpenDepartureTasks)
            return Result.Fail("This user still has open tasks assigned. Reassign or complete them first, or disable the user instead.");

        return await _db.InTransactionAsync(async () =>
        {
            var linked = await _db.Employees.IgnoreQueryFilters().Where(e => e.UserId == id).ToListAsync(ct);
            foreach (var employee in linked) employee.UserId = null;
            await _db.SaveChangesAsync(ct);

            var deleted = await _users.DeleteAsync(user);
            if (!deleted.Succeeded) return Result.Fail(Describe(deleted));

            await _audit.LogAsync("DeleteUser", "User", id, user.Email, ct);
            return Result.Success();
        }, ct);
    }

    private async Task<string?> ValidateRolesAsync(IEnumerable<string> roles, CancellationToken ct)
    {
        var existing = await _roles.Roles.Select(r => r.Name!).ToListAsync(ct);
        var unknown = roles.Where(r => !existing.Contains(r)).ToList();
        return unknown.Count > 0 ? $"Unknown role(s): {string.Join(", ", unknown)}." : null;
    }

    private async Task<string?> LinkEmployeeAsync(string userId, int? employeeId, CancellationToken ct)
    {
        var current = await _db.Employees.Where(e => e.UserId == userId).ToListAsync(ct);
        if (employeeId.HasValue && current.Any(e => e.Id == employeeId.Value)) return null;

        foreach (var employee in current) employee.UserId = null;

        if (employeeId.HasValue)
        {
            var target = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId.Value, ct);
            if (target is null) return "Selected employee not found.";
            if (target.UserId is not null) return $"{target.Name} is already linked to another user.";
            target.UserId = userId;
        }

        await _db.SaveChangesAsync(ct);
        return null;
    }

    private async Task DisableAsync(ApplicationUser user)
    {
        await _users.SetLockoutEnabledAsync(user, true);
        await _users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await _users.UpdateSecurityStampAsync(user); // signs out existing sessions
    }

    private async Task<int> CountActiveAdminsAsync()
        => (await _users.GetUsersInRoleAsync(Roles.Admin)).Count(u => !IsDisabled(u));

    private static bool IsDisabled(ApplicationUser user)
        => user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;

    private static string Describe(IdentityResult result)
        => string.Join(" ", result.Errors.Select(e => e.Description));
}
