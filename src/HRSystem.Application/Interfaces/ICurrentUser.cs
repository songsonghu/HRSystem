namespace HRSystem.Application.Interfaces;

/// <summary>
/// Provides information about the currently authenticated user. Implemented in
/// the Web layer over IHttpContextAccessor.
/// </summary>
public interface ICurrentUser
{
    string? UserId { get; }
    string? UserName { get; }

    /// <summary>Member of the built-in Admin role, which implicitly holds every permission.</summary>
    bool IsAdmin { get; }

    bool HasPermission(string permission);
}
