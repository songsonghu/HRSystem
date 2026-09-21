using HRSystem.Domain.Common;
using HRSystem.Domain.Enums;

namespace HRSystem.Domain.Entities;

public class DepartureTask : BaseEntity
{
    public int DepartureRequestId { get; set; }
    public int? AssignedDepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public DepartureTaskStatus Status { get; set; } = DepartureTaskStatus.NotStarted;
    public string? TaskRemark { get; set; }
    public bool IsRequired { get; set; } = true;
    public int SortOrder { get; set; }
    public string? HandledBy { get; set; }
    public DateTime? CompletedAt { get; set; }

    public DepartureRequest? DepartureRequest { get; set; }
    public ICollection<DepartureTaskItem> Items { get; set; } = new List<DepartureTaskItem>();
}
