namespace HRSystem.Domain.Entities;

/// <summary>
/// An immutable audit record for compliance traceability. Every significant
/// action (submit, open, disable, export, etc.) writes one row.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }

    /// <summary>Identity user id who performed the action.</summary>
    public string? UserId { get; set; }

    /// <summary>User display name (snapshot).</summary>
    public string? UserName { get; set; }

    /// <summary>Action verb, e.g. "SubmitRequest", "OpenAccount".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Entity name affected, e.g. "AccountRequest".</summary>
    public string? EntityName { get; set; }

    /// <summary>Entity id affected.</summary>
    public string? EntityId { get; set; }

    /// <summary>Additional serialized detail (JSON or text).</summary>
    public string? Detail { get; set; }

    /// <summary>UTC timestamp of the action.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
