using HRSystem.Domain.Common;
using HRSystem.Domain.Enums;

namespace HRSystem.Domain.Entities;

/// <summary>
/// A single line of an account request: one account type assigned to one
/// department. This is the unit of work the department head acts on and
/// the core of the parallel multi-party workflow.
/// </summary>
public class AccountRequestItem : BaseEntity
{
    /// <summary>Foreign key to the parent request.</summary>
    public int RequestId { get; set; }

    /// <summary>Foreign key to the requested account type.</summary>
    public int AccountTypeId { get; set; }

    /// <summary>Department the item is dispatched to (snapshot at submit time).</summary>
    public int AssignedDeptId { get; set; }

    /// <summary>Identity user id of the assigned department head.</summary>
    public string? AssignedUserId { get; set; }

    /// <summary>Current item status (NotStarted / WIP / Completed / KIV / Rejected).</summary>
    public ItemStatus Status { get; set; } = ItemStatus.NotStarted;

    /// <summary>Provisioned account value (login name) filled by the department.</summary>
    public string? AccountValue { get; set; }

    /// <summary>Result remark / reason entered by the department head.</summary>
    public string? ResultRemark { get; set; }

    /// <summary>Identity user id of the person who handled the item.</summary>
    public string? HandledBy { get; set; }

    /// <summary>UTC timestamp when the item was handled.</summary>
    public DateTime? HandledAt { get; set; }

    // Navigation
    public AccountRequest? Request { get; set; }
    public AccountType? AccountType { get; set; }
    public Department? AssignedDept { get; set; }
}
