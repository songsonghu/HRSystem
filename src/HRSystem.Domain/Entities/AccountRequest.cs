using HRSystem.Domain.Common;
using HRSystem.Domain.Enums;

namespace HRSystem.Domain.Entities;

/// <summary>
/// The master ticket for an account provisioning / de-provisioning request.
/// A request fans out into multiple <see cref="AccountRequestItem"/> rows,
/// each handled independently by the responsible department.
/// </summary>
public class AccountRequest : BaseEntity
{
    /// <summary>Human-friendly reference number, e.g. "REQ-2026-00012".</summary>
    public string RequestNo { get; set; } = string.Empty;

    /// <summary>Foreign key to the target employee.</summary>
    public int EmployeeId { get; set; }

    /// <summary>What kind of request this is (onboard / add / remove / offboard).</summary>
    public RequestType RequestType { get; set; }

    /// <summary>Overall status of the request.</summary>
    public RequestStatus Status { get; set; } = RequestStatus.Draft;

    /// <summary>Free-text remark entered by HR.</summary>
    public string? Remark { get; set; }

    /// <summary>Identity user id of the HR person who applied.</summary>
    public string? AppliedBy { get; set; }

    /// <summary>UTC timestamp when the request was submitted.</summary>
    public DateTime? AppliedAt { get; set; }

    /// <summary>UTC timestamp when the request was fully completed.</summary>
    public DateTime? CompletedAt { get; set; }

    // Navigation
    public Employee? Employee { get; set; }
    public ICollection<AccountRequestItem> Items { get; set; } = new List<AccountRequestItem>();
    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
}
