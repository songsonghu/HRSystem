namespace HRSystem.Application.Interfaces;

/// <summary>Result of saving a file to storage.</summary>
public record StoredFile(string FileName, string RelativePath, string? ContentType, long Size);

/// <summary>
/// Abstracts persistence of scanned approval documents. The default
/// implementation writes to a configured local/SMB folder; it can be swapped
/// for Azure Blob without touching the Application layer.
/// </summary>
public interface IFileStorageService
{
    Task<StoredFile> SaveAsync(
        Stream content,
        string originalFileName,
        string? contentType,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);

    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
}
