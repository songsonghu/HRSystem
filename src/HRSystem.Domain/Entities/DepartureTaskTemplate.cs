using HRSystem.Domain.Common;

namespace HRSystem.Domain.Entities;

public class DepartureTaskTemplate : BaseEntity
{
    public string DepartmentName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsRequired { get; set; } = true;
    public int SortOrder { get; set; }

    public ICollection<DepartureTaskTemplateItem> Items { get; set; } = new List<DepartureTaskTemplateItem>();
}
