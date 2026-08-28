using System.Security.Claims;
using HRSystem.Application.Interfaces;

namespace HRSystem.Web.Services;

/// <summary>
/// Reads the authenticated user's identity from the current HTTP context and
/// exposes it to the Application layer via <see cref="ICurrentUser"/>.
/// </summary>
public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public string? UserId => Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? UserName => Principal?.Identity?.Name;

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;
}
