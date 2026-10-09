using HRSystem.Domain.Common;

namespace HRSystem.Domain.Entities;

public class ChecklistTaskItem : BaseEntity
{
    public int ChecklistTaskId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; } = true;
    public bool IsCompleted { get; set; }
    public bool IsNotApplicable { get; set; }
    public string? Remark { get; set; }
    public string? CompletedBy { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ChecklistTask? ChecklistTask { get; set; }
}
