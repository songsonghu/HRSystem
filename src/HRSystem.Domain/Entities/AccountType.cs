using HRSystem.Domain.Common;

namespace HRSystem.Domain.Entities;

/// <summary>
/// A configurable account type dictionary (e.g. PC Domain, E-mail, Ayers, IBO).
/// Adding a new account type is data configuration only - no code change needed.
/// Each account type maps to the department responsible for provisioning it.
/// </summary>
public class AccountType : BaseEntity
{
    /// <summary>Unique code, e.g. "PCDomain", "Email", "Ayers", "IBO".</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable name, e.g. "PC Domain Account".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description / provisioning notes shown to the department head.</summary>
    public string? Description { get; set; }

    /// <summary>Foreign key to the responsible department.</summary>
    public int ResponsibleDeptId { get; set; }

    /// <summary>Whether this account type can be requested.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Display order in the request form.</summary>
    public int SortOrder { get; set; }

    // Navigation
    public Department? ResponsibleDept { get; set; }
}
