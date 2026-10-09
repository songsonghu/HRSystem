using HRSystem.Domain.Enums;

namespace HRSystem.Application.DTOs;

/// <summary>Input for a new onboarding or departure draft. Kind-specific fields are ignored for the other kind.</summary>
public class CreateChecklistRequestDto
{
    public ChecklistKind Kind { get; set; }
    public int EmployeeId { get; set; }

    // Onboarding
    public DateTime? StartDate { get; set; }
    public List<int> AccountTypeIds { get; set; } = new();
    public Dictionary<int, string> AccountDetails { get; set; } = new();

    // Departure
    public DateTime? LastWorkingDate { get; set; }
    public DateTime? LastEmploymentDate { get; set; }
    public string? Reason { get; set; }

    public string? Remark { get; set; }
}

public class ChecklistRequestDto
{
    public int Id { get; set; }
    public ChecklistKind Kind { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public int EmployeeId { get; set; }
    public string EmployeeNo { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public ChecklistStatus Status { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? LastWorkingDate { get; set; }
    public DateTime? LastEmploymentDate { get; set; }
    public string? Reason { get; set; }
    public string? Remark { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public int? AccountRequestId { get; set; }
    public string? AccountRequestNo { get; set; }
    public RequestStatus? AccountRequestStatus { get; set; }

    public List<ChecklistTaskDto> Tasks { get; set; } = new();

    public bool CanSubmit => Status == ChecklistStatus.Draft;
    public bool CanFinalize => Status == ChecklistStatus.PendingFinalReview;
}

public class ChecklistTaskDto
{
    public int Id { get; set; }
    public int ChecklistRequestId { get; set; }
    public ChecklistKind Kind { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public string EmployeeNo { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public ChecklistTaskStatus Status { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public string? TaskRemark { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<ChecklistTaskItemDto> Items { get; set; } = new();
}

public class ChecklistTaskItemDto
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsNotApplicable { get; set; }
    public string? Remark { get; set; }
}

public class ChecklistTaskUpdateDto
{
    public int TaskId { get; set; }
    public string? TaskRemark { get; set; }
    public bool MarkAsCompleted { get; set; }
    public List<ChecklistTaskItemUpdateDto> Items { get; set; } = new();
}

public class ChecklistTaskItemUpdateDto
{
    public int Id { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsNotApplicable { get; set; }
    public string? Remark { get; set; }
}

/// <summary>What the create page shows for the chosen employee.</summary>
public class ChecklistCreatePageDto
{
    public ChecklistKind Kind { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeNo { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Position { get; set; }
    public DateTime JoinDate { get; set; }
    public IReadOnlyList<ChecklistTemplatePreviewDto> Templates { get; set; } = Array.Empty<ChecklistTemplatePreviewDto>();

    /// <summary>Onboarding: account types on this employee's requisition form.</summary>
    public IReadOnlyList<AccountTypeOptionDto> AccountTypeOptions { get; set; } = Array.Empty<AccountTypeOptionDto>();
}

public class ChecklistTemplatePreviewDto
{
    public string DepartmentName { get; set; } = string.Empty;
    public int ItemCount { get; set; }

    /// <summary>False when the department has no manager with a login, so submitting would fail.</summary>
    public bool CanReceiveTasks { get; set; }
}

// ------------------------------------------------------------- templates

public class ChecklistTemplateListItemDto
{
    public int Id { get; set; }
    public ChecklistKind Kind { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public int ItemCount { get; set; }
    public bool CanReceiveTasks { get; set; }
}

public class ChecklistTemplateEditDto
{
    public int Id { get; set; }
    public ChecklistKind Kind { get; set; }
    public int? DepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsRequired { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>Rows with an empty description are ignored, so the form can offer blank rows to add items.</summary>
    public List<ChecklistTemplateItemEditDto> Items { get; set; } = new();
}

public class ChecklistTemplateItemEditDto
{
    public int Id { get; set; }
    public string? Description { get; set; }
    public bool IsRequired { get; set; } = true;
    public int SortOrder { get; set; }
}
