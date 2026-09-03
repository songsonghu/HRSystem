using HRSystem.Domain.Enums;

namespace HRSystem.Application.DTOs;

/// <summary>Read model for an employee attachment.</summary>
public class EmployeeAttachmentDto
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long FileSize { get; set; }
}

/// <summary>Read model for an employee row.</summary>
public class EmployeeDto
{
    public int Id { get; set; }
    public string EmployeeNo { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Department { get; set; }
    public string? Position { get; set; }
    public DateTime JoinDate { get; set; }
    public DateTime? ResignDate { get; set; }
    public EmployeeStatus Status { get; set; }
    public EmployeeCategory Category { get; set; }
    public IReadOnlyList<EmployeeAttachmentDto> Attachments { get; set; } = Array.Empty<EmployeeAttachmentDto>();
}

/// <summary>Create/update model for an employee.</summary>
public class EmployeeEditDto
{
    public int Id { get; set; }
    public string EmployeeNo { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Department { get; set; }
    public string? Position { get; set; }
    public DateTime JoinDate { get; set; } = DateTime.Today;
    public DateTime? ResignDate { get; set; }
    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;
    public EmployeeCategory Category { get; set; } = EmployeeCategory.Staff;
    public IReadOnlyList<EmployeeAttachmentDto> Attachments { get; set; } = Array.Empty<EmployeeAttachmentDto>();
}
