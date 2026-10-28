using HRSystem.Domain.Common;
using HRSystem.Domain.Enums;

namespace HRSystem.Domain.Entities;

/// <summary>One department's part of a checklist request, copied from its template at submit time. Its department's departure handlers process it and its departure approvers approve it.</summary>
public class ChecklistTask : BaseEntity
{
    public int ChecklistRequestId { get; set; }
    public int? AssignedDepartmentId { get; set; }

    /// <summary>Department name snapshot at submit time.</summary>
    public string DepartmentName { get; set; } = string.Empty;
    public ChecklistTaskStatus Status { get; set; } = ChecklistTaskStatus.NotStarted;
    public string? TaskRemark { get; set; }
    public bool IsRequired { get; set; } = true;
    public int SortOrder { get; set; }
    public string? HandledBy { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>The department approver who accepted the completed task, and when.</summary>
    public string? ApprovedBy { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTime? ApprovedAt { get; set; }

    /// <summary>Why the department approver or the final approver sent the task back; cleared when it is completed again.</summary>
    public string? ReturnReason { get; set; }
    public DateTime? ReturnedAt { get; set; }

    public ChecklistRequest? ChecklistRequest { get; set; }
    public ICollection<ChecklistTaskItem> Items { get; set; } = new List<ChecklistTaskItem>();
}
