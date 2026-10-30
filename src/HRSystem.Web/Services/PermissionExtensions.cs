using System.Security.Claims;
using HRSystem.Application.Security;
using HRSystem.Infrastructure.Identity;

namespace HRSystem.Web.Services;

public static class PermissionExtensions
{
    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(Roles.Admin);

    public static bool HasPermission(this ClaimsPrincipal user, string permission)
        => user.IsAdmin() || user.HasClaim(Permissions.ClaimType, permission);
}
