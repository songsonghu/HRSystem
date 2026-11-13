using HRSystem.Domain.Common;

namespace HRSystem.Domain.Entities;

public class ChecklistTemplateItem : BaseEntity
{
    public int ChecklistTemplateId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; } = true;

    /// <summary>Departure: the assignee may attach one image or PDF to this item.</summary>
    public bool AllowAttachment { get; set; }

    public ChecklistTemplate? ChecklistTemplate { get; set; }
}
