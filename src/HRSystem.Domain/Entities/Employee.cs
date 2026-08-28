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

    /// <summary>Personal / work email used for the welcome notification.</summary>
    public string? Email { get; set; }

    /// <summary>Department name the employee belongs to.</summary>
    public string? Department { get; set; }

    /// <summary>Job title / position.</summary>
    public string? Position { get; set; }

    /// <summary>Date the employee joined.</summary>
    public DateTime JoinDate { get; set; }

    /// <summary>Date the employee resigned (null while still employed).</summary>
    public DateTime? ResignDate { get; set; }

    /// <summary>Current employment status.</summary>
    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;

    // Navigation
    public ICollection<AccountRequest> AccountRequests { get; set; } = new List<AccountRequest>();
    public ICollection<EmployeeAccount> Accounts { get; set; } = new List<EmployeeAccount>();
}
