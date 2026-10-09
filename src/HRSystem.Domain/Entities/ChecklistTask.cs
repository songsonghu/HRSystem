using HRSystem.Domain.Common;
using HRSystem.Domain.Enums;

namespace HRSystem.Domain.Entities;

/// <summary>One department's part of a checklist request, copied from its template at submit time.</summary>
public class ChecklistTask : BaseEntity
{
    public int ChecklistRequestId { get; set; }
    public int? AssignedDepartmentId { get; set; }

    /// <summary>Department name snapshot at submit time.</summary>
    public string DepartmentName { get; set; } = string.Empty;
    public string? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public ChecklistTaskStatus Status { get; set; } = ChecklistTaskStatus.NotStarted;
    public string? TaskRemark { get; set; }
    public bool IsRequired { get; set; } = true;
    public int SortOrder { get; set; }
    public string? HandledBy { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ChecklistRequest? ChecklistRequest { get; set; }
    public ICollection<ChecklistTaskItem> Items { get; set; } = new List<ChecklistTaskItem>();
}
