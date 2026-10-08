using HRSystem.Domain.Common;
using HRSystem.Domain.Enums;

namespace HRSystem.Domain.Entities;

/// <summary>
/// An employee master record. Drives onboarding, in-service account changes,
/// and offboarding workflows.
/// </summary>
public class Employee : BaseEntity
{
    /// <summary>Company employee number, unique.</summary>
    public string EmployeeNo { get; set; } = string.Empty;

    /// <summary>Full name.</summary>
    public string Name { get; set; } = string.Empty;

    public Gender Gender { get; set; } = Gender.Unspecified;

    /// <summary>Personal / work email used for the welcome notification.</summary>
    public string? Email { get; set; }

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    /// <summary>Job title / position.</summary>
    public string? Position { get; set; }

    /// <summary>Date the employee joined.</summary>
    public DateTime JoinDate { get; set; }

    /// <summary>Date the employee resigned (null while still employed).</summary>
    public DateTime? ResignDate { get; set; }

    /// <summary>Current employment status.</summary>
    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;

    /// <summary>
    /// Employee category (Staff / AE). Determines which requisition form
    /// layout and account type checklist is used when raising a request.
    /// </summary>
    public EmployeeCategory Category { get; set; } = EmployeeCategory.Staff;

    /// <summary>Identity user id of this employee's login account, if any (one-to-one).</summary>
    public string? UserId { get; set; }

    // Navigation
    public ICollection<AccountRequest> AccountRequests { get; set; } = new List<AccountRequest>();
    public ICollection<DepartureRequest> DepartureRequests { get; set; } = new List<DepartureRequest>();
    public ICollection<EmployeeAccount> Accounts { get; set; } = new List<EmployeeAccount>();
    public ICollection<EmployeeAttachment> Attachments { get; set; } = new List<EmployeeAttachment>();
}
