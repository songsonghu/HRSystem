using Microsoft.AspNetCore.Identity;

namespace HRSystem.Infrastructure.Identity;

/// <summary>
/// Application user backed by ASP.NET Core Identity, extended with a display name.
/// The user's department comes from the linked employee record (Employee.UserId).
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }
}

/// <summary>Well-known role names used by the authorization policies.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string HR = "HR";
    public const string DeptHead = "DeptHead";
}
