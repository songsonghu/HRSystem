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

    public Department? Department { get; set; }
    public ICollection<ChecklistTemplateItem> Items { get; set; } = new List<ChecklistTemplateItem>();
}
