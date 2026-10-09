using System.Security.Claims;
using HRSystem.Application.Common;
using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using HRSystem.Infrastructure.Identity;
using HRSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Infrastructure.Services;

/// <summary>Role management; permissions are stored as role claims.</summary>
public class RoleAdminService : IRoleAdminService
{
    private readonly RoleManager<IdentityRole> _roles;
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public RoleAdminService(RoleManager<IdentityRole> roles, AppDbContext db, IAuditService audit)
    {
        _roles = roles;
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<RoleListItemDto>> GetListAsync(CancellationToken ct = default)
    {
        var roles = await _roles.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        var permissions = (await _db.RoleClaims.AsNoTracking()
                .Where(c => c.ClaimType == Permissions.ClaimType)
                .Select(c => new { c.RoleId, c.ClaimValue })
                .ToListAsync(ct))
            .ToLookup(c => c.RoleId, c => c.ClaimValue!);
        var userCounts = await _db.UserRoles
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, ct);

        return roles.Select(r => new RoleListItemDto
        {
            Id = r.Id,
            Name = r.Name!,
            UserCount = userCounts.GetValueOrDefault(r.Id),
            IsProtected = IsProtected(r),
            Permissions = IsProtected(r)
                ? Permissions.All.Select(p => p.Name).ToList()
                : permissions[r.Id].ToList()
        }).ToList();
    }

    public async Task<RoleEditDto?> GetAsync(string id, CancellationToken ct = default)
    {
        var role = await _roles.FindByIdAsync(id);
        if (role is null) return null;

        return new RoleEditDto
        {
            Id = role.Id,
            Name = role.Name!,
            IsProtected = IsProtected(role),
            Permissions = IsProtected(role)
                ? Permissions.All.Select(p => p.Name).ToList()
                : await GetPermissionsAsync(role)
        };
    }

    public async Task<Result<string>> CreateAsync(RoleEditDto dto, CancellationToken ct = default)
    {
        var name = dto.Name.Trim();
        if (await _roles.RoleExistsAsync(name)) return Result<string>.Fail($"Role '{name}' already exists.");

        return await _db.InTransactionAsync(async () =>
        {
            var role = new IdentityRole(name);
            var created = await _roles.CreateAsync(role);
            if (!created.Succeeded) return Result<string>.Fail(Describe(created));

            foreach (var permission in dto.Permissions.Distinct())
            {
                var added = await _roles.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission));
                if (!added.Succeeded) return Result<string>.Fail(Describe(added));
            }

            await _audit.LogAsync("CreateRole", "Role", role.Id, $"{name}: {string.Join(",", dto.Permissions)}", ct);
            return Result<string>.Success(role.Id);
        }, ct);
    }

    public async Task<Result> UpdateAsync(RoleEditDto dto, CancellationToken ct = default)
    {
        var role = await _roles.FindByIdAsync(dto.Id ?? string.Empty);
        if (role is null) return Result.Fail("Role not found.");
        if (IsProtected(role)) return Result.Fail($"The {Roles.Admin} role is built in and cannot be changed.");

        var name = dto.Name.Trim();
        if (!string.Equals(name, role.Name, StringComparison.OrdinalIgnoreCase) && await _roles.RoleExistsAsync(name))
            return Result.Fail($"Role '{name}' already exists.");

        return await _db.InTransactionAsync(async () =>
        {
            if (name != role.Name)
            {
                var renamed = await _roles.SetRoleNameAsync(role, name);
                if (!renamed.Succeeded) return Result.Fail(Describe(renamed));
                var updated = await _roles.UpdateAsync(role);
                if (!updated.Succeeded) return Result.Fail(Describe(updated));
            }

            var current = await GetPermissionsAsync(role);
            foreach (var permission in current.Except(dto.Permissions))
            {
                var removed = await _roles.RemoveClaimAsync(role, new Claim(Permissions.ClaimType, permission));
                if (!removed.Succeeded) return Result.Fail(Describe(removed));
            }
            foreach (var permission in dto.Permissions.Distinct().Except(current))
            {
                var added = await _roles.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission));
                if (!added.Succeeded) return Result.Fail(Describe(added));
            }

            await _audit.LogAsync("UpdateRole", "Role", role.Id, $"{name}: {string.Join(",", dto.Permissions)}", ct);
            return Result.Success();
        }, ct);
    }

    public async Task<Result> DeleteAsync(string id, CancellationToken ct = default)
    {
        var role = await _roles.FindByIdAsync(id);
        if (role is null) return Result.Fail("Role not found.");
        if (IsProtected(role)) return Result.Fail($"The {Roles.Admin} role is built in and cannot be deleted.");

        var userCount = await _db.UserRoles.CountAsync(ur => ur.RoleId == id, ct);
        if (userCount > 0)
            return Result.Fail($"{userCount} user(s) still have this role. Remove it from them first.");

        var deleted = await _roles.DeleteAsync(role);
        if (!deleted.Succeeded) return Result.Fail(Describe(deleted));

        await _audit.LogAsync("DeleteRole", "Role", id, role.Name, ct);
        return Result.Success();
    }

    private async Task<List<string>> GetPermissionsAsync(IdentityRole role)
        => (await _roles.GetClaimsAsync(role))
            .Where(c => c.Type == Permissions.ClaimType)
            .Select(c => c.Value)
            .ToList();

    private static bool IsProtected(IdentityRole role) => role.Name == Roles.Admin;

    private static string Describe(IdentityResult result)
        => string.Join(" ", result.Errors.Select(e => e.Description));
}
