using HRSystem.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace HRSystem.Infrastructure.Services;

/// <summary>File storage settings bound from configuration ("FileStorage").</summary>
public class FileStorageOptions
{
    /// <summary>Absolute or relative root folder for uploaded documents.</summary>
    public string RootPath { get; set; } = "App_Data/uploads";
}

/// <summary>
/// Stores scanned approval documents on the local/SMB file system, organized
/// by year/month. Only metadata is kept in the database.
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private readonly string _root;

    public LocalFileStorageService(IOptions<FileStorageOptions> options)
    {
        _root = Path.GetFullPath(options.Value.RootPath);
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredFile> SaveAsync(
        Stream content, string originalFileName, string? contentType, CancellationToken cancellationToken = default)
    {
        var subDir = Path.Combine(DateTime.UtcNow.ToString("yyyy"), DateTime.UtcNow.ToString("MM"));
        var absDir = Path.Combine(_root, subDir);
        Directory.CreateDirectory(absDir);

        var safeName = Path.GetFileName(originalFileName);
        var unique = $"{Guid.NewGuid():N}_{safeName}";
        var absPath = Path.Combine(absDir, unique);

        await using (var fs = new FileStream(absPath, FileMode.CreateNew, FileAccess.Write))
        {
            await content.CopyToAsync(fs, cancellationToken);
        }

        var relative = Path.Combine(subDir, unique).Replace('\\', '/');
        var size = new FileInfo(absPath).Length;
        return new StoredFile(safeName, relative, contentType, size);
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var absPath = Path.Combine(_root, relativePath);
        Stream stream = new FileStream(absPath, FileMode.Open, FileAccess.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var absPath = Path.Combine(_root, relativePath);
        if (File.Exists(absPath)) File.Delete(absPath);
        return Task.CompletedTask;
    }
}
