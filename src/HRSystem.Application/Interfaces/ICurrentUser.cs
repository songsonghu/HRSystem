namespace HRSystem.Application.Interfaces;

/// <summary>
/// Provides information about the currently authenticated user. Implemented in
/// the Web layer over IHttpContextAccessor.
/// </summary>
public interface ICurrentUser
{
    string? UserId { get; }
    string? UserName { get; }
    bool IsInRole(string role);
}
