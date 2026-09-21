using HRSystem.Domain.Enums;

namespace HRSystem.Application.DTOs;

public class CreateDepartureRequestDto
{
    public int EmployeeId { get; set; }
    public DateTime LastWorkingDate { get; set; }
    public DateTime LastEmploymentDate { get; set; }
    public string? Reason { get; set; }
    public string? Remark { get; set; }
}

public class DepartureRequestDto
{
    public int Id { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public int EmployeeId { get; set; }
    public string EmployeeNo { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DepartureRequestStatus Status { get; set; }
    public DateTime LastWorkingDate { get; set; }
    public DateTime LastEmploymentDate { get; set; }
    public string? Reason { get; set; }
    public string? Remark { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<DepartureTaskDto> Tasks { get; set; } = new();
}

public class DepartureTaskDto
{
    public int Id { get; set; }
    public int DepartureRequestId { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public string EmployeeNo { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public DepartureTaskStatus Status { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public string? TaskRemark { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<DepartureTaskItemDto> Items { get; set; } = new();
}

public class DepartureTaskItemDto
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsNotApplicable { get; set; }
    public string? Remark { get; set; }
}

public class DepartureTaskUpdateDto
{
    public int TaskId { get; set; }
    public string? TaskRemark { get; set; }
    public bool MarkAsCompleted { get; set; }
    public List<DepartureTaskItemUpdateDto> Items { get; set; } = new();
}

public class DepartureTaskItemUpdateDto
{
    public int Id { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsNotApplicable { get; set; }
    public string? Remark { get; set; }
}
