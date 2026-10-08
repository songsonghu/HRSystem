using HRSystem.Application.Common;
using HRSystem.Application.DTOs;

namespace HRSystem.Application.Interfaces;

/// <summary>User management (login accounts, role membership, employee link).</summary>
public interface IUserAdminService
{
    Task<IReadOnlyList<UserListItemDto>> GetListAsync(string? keyword = null, CancellationToken ct = default);
    Task<UserEditDto?> GetAsync(string id, CancellationToken ct = default);
    Task<UserFormOptionsDto> GetFormOptionsAsync(string? userId, CancellationToken ct = default);
    Task<Result<string>> CreateAsync(UserEditDto dto, CancellationToken ct = default);
    Task<Result> UpdateAsync(UserEditDto dto, CancellationToken ct = default);
    Task<Result> ResetPasswordAsync(ResetPasswordDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(string id, CancellationToken ct = default);
}

/// <summary>Role management (role names and their permissions).</summary>
public interface IRoleAdminService
{
    Task<IReadOnlyList<RoleListItemDto>> GetListAsync(CancellationToken ct = default);
    Task<RoleEditDto?> GetAsync(string id, CancellationToken ct = default);
    Task<Result<string>> CreateAsync(RoleEditDto dto, CancellationToken ct = default);
    Task<Result> UpdateAsync(RoleEditDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(string id, CancellationToken ct = default);
}
