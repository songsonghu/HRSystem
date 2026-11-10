using HRSystem.Domain.Common;
using HRSystem.Domain.Enums;

namespace HRSystem.Domain.Entities;

/// <summary>A department's onboarding or departure checklist. Editing it does not affect submitted requests.</summary>
public class ChecklistTemplate : BaseEntity
{
    public ChecklistKind Kind { get; set; }
    public int DepartmentId { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Whether the request can only be finalized once this department's task is done.</summary>
    public bool IsRequired { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>
    /// AE Code: the task records the leaver's AE codes, and its handlers and approvers are set apart
    /// (<see cref="WorkArea.DepartureAeCode"/>), so a department can have one template of each type.
    /// </summary>
    public ChecklistTaskType TaskType { get; set; } = ChecklistTaskType.Checklist;

    public Department? Department { get; set; }
    public ICollection<ChecklistTemplateItem> Items { get; set; } = new List<ChecklistTemplateItem>();
}
