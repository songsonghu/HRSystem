using HRSystem.Domain.Common;

namespace HRSystem.Domain.Entities;

/// <summary>
/// An organizational department. Each department has a head (an Identity user)
/// who is responsible for opening/disabling the account types assigned to it.
/// </summary>
public class Department : BaseEntity
{
    /// <summary>Department display name, e.g. "IT Infrastructure".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional short code, e.g. "ITINFRA".</summary>
    public string? Code { get; set; }

    /// <summary>Identity user id of the department head (responsible person).</summary>
    public string? HeadUserId { get; set; }

    /// <summary>Whether the department is active and can be assigned work.</summary>
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<AccountType> AccountTypes { get; set; } = new List<AccountType>();
}
