using HRSystem.Domain.Common;
using HRSystem.Domain.Enums;

namespace HRSystem.Domain.Entities;

public class DepartureRequest : BaseEntity
{
    public string RequestNo { get; set; } = string.Empty;
    public int EmployeeId { get; set; }
    public DepartureRequestStatus Status { get; set; } = DepartureRequestStatus.Draft;
    public DateTime LastWorkingDate { get; set; }
    public DateTime LastEmploymentDate { get; set; }
    public string? Reason { get; set; }
    public string? Remark { get; set; }

    public string? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? FinalizedBy { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public Employee? Employee { get; set; }
    public ICollection<DepartureTask> Tasks { get; set; } = new List<DepartureTask>();
}
