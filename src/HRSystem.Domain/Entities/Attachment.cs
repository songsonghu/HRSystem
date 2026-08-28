using HRSystem.Domain.Common;

namespace HRSystem.Domain.Entities;

/// <summary>
/// A scanned, signed approval document attached to an account request.
/// Files are stored on disk; only metadata is persisted in the database.
/// </summary>
public class Attachment : BaseEntity
{
    /// <summary>Foreign key to the owning request.</summary>
    public int RequestId { get; set; }

    /// <summary>Original file name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Relative storage path or key.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>MIME content type.</summary>
    public string? ContentType { get; set; }

    /// <summary>File size in bytes.</summary>
    public long FileSize { get; set; }

    // Navigation
    public AccountRequest? Request { get; set; }
}
