using HRSystem.Domain.Common;

namespace HRSystem.Domain.Entities;

public class DepartureTaskTemplateItem : BaseEntity
{
    public int DepartureTaskTemplateId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; } = true;

    public DepartureTaskTemplate? DepartureTaskTemplate { get; set; }
}
