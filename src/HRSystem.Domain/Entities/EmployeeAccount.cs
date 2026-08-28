using HRSystem.Domain.Common;
using HRSystem.Domain.Enums;

namespace HRSystem.Domain.Entities;

/// <summary>
/// The standing ledger of accounts an employee actually holds. Populated when a
/// request item is completed. This is the source of truth for the offboarding
/// account listing / export.
/// </summary>
public class EmployeeAccount : BaseEntity
{
    /// <summary>Foreign key to the employee.</summary>
    public int EmployeeId { get; set; }

    /// <summary>Foreign key to the account type.</summary>
    public int AccountTypeId { get; set; }

    /// <summary>The provisioned account value (login name / id).</summary>
    public string? AccountValue { get; set; }

    /// <summary>Current status (Active / Disabled).</summary>
    public AccountStatus Status { get; set; } = AccountStatus.Active;

    /// <summary>UTC timestamp when the account was opened.</summary>
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when the account was disabled.</summary>
    public DateTime? DisabledAt { get; set; }

    /// <summary>The request item that created this ledger row (traceability).</summary>
    public int? SourceRequestItemId { get; set; }

    // Navigation
    public Employee? Employee { get; set; }
    public AccountType? AccountType { get; set; }
}
