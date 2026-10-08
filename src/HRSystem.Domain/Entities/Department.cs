using HRSystem.Domain.Common;

namespace HRSystem.Domain.Entities;

/// <summary>
/// An organizational department. Its manager is an employee; tasks routed to the
/// department are assigned to the login account linked to that employee.
/// </summary>
public class Department : BaseEntity
{
    /// <summary>Department display name, e.g. "IT Infrastructure". Unique.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional short code, e.g. "ITINFRA".</summary>
    public string? Code { get; set; }

    public int? ManagerEmployeeId { get; set; }
    public Employee? Manager { get; set; }

    /// <summary>Whether the department is active and can be assigned work.</summary>
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
    public ICollection<AccountType> AccountTypes { get; set; } = new List<AccountType>();
}
