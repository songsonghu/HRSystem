using HRSystem.Domain.Enums;

namespace HRSystem.Application.DTOs;

/// <summary>Row on the department list.</summary>
public class DepartmentListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsActive { get; set; }
    public string? ManagerName { get; set; }

    /// <summary>False when a manager is set but has no login, so tasks cannot be routed to them.</summary>
    public bool ManagerCanReceiveTasks { get; set; }
    public int EmployeeCount { get; set; }
}

/// <summary>Department page: details plus its employees.</summary>
public class DepartmentDetailsDto : DepartmentListItemDto
{
    public int? ManagerEmployeeId { get; set; }
    public IReadOnlyList<DepartmentMemberDto> Employees { get; set; } = Array.Empty<DepartmentMemberDto>();
}

public class DepartmentMemberDto
{
    public int Id { get; set; }
    public string EmployeeNo { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Position { get; set; }
    public EmployeeStatus Status { get; set; }
    public bool HasLogin { get; set; }
}

/// <summary>Create/edit model for a department.</summary>
public class DepartmentEditDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public int? ManagerEmployeeId { get; set; }
    public bool IsActive { get; set; } = true;
}

public record DepartmentOptionDto(int Id, string Name);
