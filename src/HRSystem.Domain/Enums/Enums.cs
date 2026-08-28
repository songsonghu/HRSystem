namespace HRSystem.Domain.Enums;

/// <summary>Employment status of an employee.</summary>
public enum EmployeeStatus
{
    Active = 0,     // Currently employed
    Resigned = 1    // Left the company
}

/// <summary>
/// The kind of account request. A single request table serves the whole
/// lifecycle: onboarding, in-service add/remove, and offboarding.
/// </summary>
public enum RequestType
{
    Onboard = 0,    // New joiner: open a set of accounts
    Add = 1,        // In-service: add a new account type
    Remove = 2,     // In-service: remove an existing account
    Offboard = 3    // Resignation: disable all accounts
}

/// <summary>Overall status of an account request (the master ticket).</summary>
public enum RequestStatus
{
    Draft = 0,       // Being prepared by HR, not submitted
    Submitted = 1,   // Submitted, emails dispatched to department heads
    InProgress = 2,  // At least one item handled, not all completed
    Completed = 3,   // All items completed
    Closed = 4       // Archived
}

/// <summary>
/// Status of a single request item (one account type handled by one department).
/// Uses the standard terminology adopted by the HR team.
/// </summary>
public enum ItemStatus
{
    NotStarted = 0,  // Waiting for the responsible department head
    WIP = 1,         // Work in progress
    Completed = 2,   // Account opened / disabled successfully
    KIV = 3,         // Keep in view (on hold / pending)
    Rejected = 4     // Cannot be processed
}

/// <summary>Status of an employee account in the standing ledger.</summary>
public enum AccountStatus
{
    Active = 0,     // Account is live
    Disabled = 1    // Account has been disabled
}
