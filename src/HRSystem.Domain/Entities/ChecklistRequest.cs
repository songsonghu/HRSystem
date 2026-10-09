using HRSystem.Domain.Common;
using HRSystem.Domain.Enums;

namespace HRSystem.Domain.Entities;

/// <summary>
/// An onboarding or departure case for one employee. On submit it fans out into one
/// <see cref="ChecklistTask"/> per active template department, each handled in parallel
/// by that department's manager.
/// </summary>
public class ChecklistRequest : BaseEntity
{
    public ChecklistKind Kind { get; set; }
    public string RequestNo { get; set; } = string.Empty;

    /// <summary>Optimistic concurrency token; departments update tasks of the same request in parallel.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public int EmployeeId { get; set; }
    public ChecklistStatus Status { get; set; } = ChecklistStatus.Draft;

    /// <summary>Onboarding: first working day.</summary>
    public DateTime? StartDate { get; set; }

    /// <summary>Departure dates.</summary>
    public DateTime? LastWorkingDate { get; set; }
    public DateTime? LastEmploymentDate { get; set; }

    public string? Reason { get; set; }
    public string? Remark { get; set; }

    /// <summary>Account request raised together with this case (onboarding: accounts to open).</summary>
    public int? AccountRequestId { get; set; }

    public string? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? FinalizedBy { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public Employee? Employee { get; set; }
    public AccountRequest? AccountRequest { get; set; }
    public ICollection<ChecklistTask> Tasks { get; set; } = new List<ChecklistTask>();
}
