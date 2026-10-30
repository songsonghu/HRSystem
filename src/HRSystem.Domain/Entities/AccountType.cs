using HRSystem.Domain.Common;
using HRSystem.Domain.Enums;

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

    /// <summary>Paper-form section name used to group this item.</summary>
    public string? GroupName { get; set; }

    /// <summary>Paper-form column: 1 for left and 2 for right.</summary>
    public int Column { get; set; } = 1;

    /// <summary>Whether the item is rendered with a selectable checkbox.</summary>
    public bool HasCheckbox { get; set; } = true;

    /// <summary>Optional text displayed before the item label.</summary>
    public string? PrefixText { get; set; }

    /// <summary>Optional text displayed after the item label.</summary>
    public string? SuffixText { get; set; }

    /// <summary>Which requisition form (Staff / AE / both) shows this item.</summary>
    public AccountTypeAudience Audience { get; set; } = AccountTypeAudience.Both;

    /// <summary>
    /// Whether HR must supply a free-text detail when selecting this item
    /// (e.g. "Group(s)/Sub-group(s)" for E-mail, "Access profile" for
    /// E-report/FXES, the license number for SFC License(s)).
    /// </summary>
    public bool RequiresDetail { get; set; }

    /// <summary>Label shown next to the detail input, e.g. "Access profile".</summary>
    public string? DetailLabel { get; set; }

    // Navigation
    public Department? ResponsibleDept { get; set; }
}
