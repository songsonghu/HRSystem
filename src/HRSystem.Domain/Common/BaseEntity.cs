namespace HRSystem.Domain.Common;

/// <summary>
/// Base entity that carries an integer primary key and common audit fields.
/// All persistent domain entities inherit from this type.
/// </summary>
public abstract class BaseEntity
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>User id (Identity) who created the record.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>UTC timestamp when the record was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User id (Identity) who last modified the record.</summary>
    public string? ModifiedBy { get; set; }

    /// <summary>UTC timestamp when the record was last modified.</summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>Soft-delete flag. Records are never physically removed.</summary>
    public bool IsDeleted { get; set; }
}
