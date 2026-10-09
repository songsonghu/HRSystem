namespace HRSystem.Application.DTOs;

/// <summary>Row on the user management list.</summary>
public class UserListItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
    public string? EmployeeDisplay { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>Create/edit model for a user. <see cref="Password"/> is only used on create.</summary>
public class UserEditDto
{
    public string? Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Password { get; set; }
    public List<string> Roles { get; set; } = new();
    public int? EmployeeId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Admin-initiated password reset.</summary>
public class ResetPasswordDto
{
    public string Id { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>Choices shown on the user form.</summary>
public class UserFormOptionsDto
{
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
    public IReadOnlyList<EmployeeOptionDto> Employees { get; set; } = Array.Empty<EmployeeOptionDto>();
}

public record EmployeeOptionDto(int Id, string Display);

/// <summary>Row on the role management list.</summary>
public class RoleListItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int UserCount { get; set; }
    public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();
    public bool IsProtected { get; set; }
}

/// <summary>Create/edit model for a role and its permissions.</summary>
public class RoleEditDto
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
    public bool IsProtected { get; set; }
}
