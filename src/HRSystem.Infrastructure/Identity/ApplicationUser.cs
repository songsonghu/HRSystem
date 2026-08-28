using Microsoft.AspNetCore.Identity;

namespace HRSystem.Infrastructure.Identity;

/// <summary>
/// Application user backed by ASP.NET Core Identity. Extended with a display
/// name and an optional department link for department-head routing.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }

    /// <summary>Optional department this user heads / belongs to.</summary>
    public int? DepartmentId { get; set; }
}

/// <summary>Well-known role names used by the authorization policies.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string HR = "HR";
    public const string DeptHead = "DeptHead";
}
