using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using HRSystem.Infrastructure.Identity;
using HRSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Infrastructure.Services;

public class UserDirectoryService : IUserDirectoryService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;

    public UserDirectoryService(UserManager<ApplicationUser> userManager, AppDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    public async Task<UserDirectoryEntry?> GetByIdAsync(string userId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        return user is null ? null : ToEntry(user);
    }

    public async Task<IReadOnlyDictionary<string, UserDirectoryEntry>> GetByIdsAsync(IEnumerable<string> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, UserDirectoryEntry>();

        var users = await _userManager.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(ct);
        return users.ToDictionary(u => u.Id, ToEntry);
    }

    public async Task<IReadOnlyList<UserDirectoryEntry>> GetActiveUsersAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var users = await _userManager.Users.AsNoTracking()
            .Where(u => u.LockoutEnd == null || u.LockoutEnd <= now)
            .ToListAsync(ct);
        return users.Select(ToEntry).OrderBy(u => u.Display).ToList();
    }

    public async Task<IReadOnlyList<UserDirectoryEntry>> GetUsersWithPermissionAsync(string permission, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var roleIds = _db.RoleClaims.Where(c => c.ClaimType == Permissions.ClaimType && c.ClaimValue == permission).Select(c => c.RoleId);
        var userIds = _db.UserRoles.Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.UserId);
        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && (u.LockoutEnd == null || u.LockoutEnd <= now))
            .ToListAsync(ct);
        return users.Select(ToEntry).OrderBy(u => u.Display).ToList();
    }

    private static UserDirectoryEntry ToEntry(ApplicationUser user) => new()
    {
        UserId = user.Id,
        UserName = user.FullName ?? user.UserName,
        Email = user.Email
    };
}
