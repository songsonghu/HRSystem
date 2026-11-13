namespace HRSystem.Application.Common;

/// <summary>What may be attached to a checklist item: one JPG, PNG or PDF file of limited size.</summary>
public static class ChecklistAttachmentRules
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const string Accept = ".jpg,.jpeg,.png,.pdf";
    public const string Description = "JPG, PNG or PDF, up to 5 MB";

    /// <summary>
    /// The content type of an allowed file, detected from its first bytes and checked against its
    /// extension; null when the file is not an allowed image or PDF.
    /// </summary>
    public static string? DetectContentType(string fileName, ReadOnlySpan<byte> content)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is ".jpg" or ".jpeg" && content.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
            return "image/jpeg";
        if (extension == ".png" && content.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return "image/png";
        if (extension == ".pdf" && content.StartsWith("%PDF-"u8))
            return "application/pdf";
        return null;
    }

    /// <summary>Validates an upload; returns the detected content type or an error message.</summary>
    public static (string? ContentType, string? Error) Validate(string fileName, byte[] content)
    {
        if (content.Length == 0) return (null, $"\"{fileName}\" is empty.");
        if (content.Length > MaxBytes) return (null, $"\"{fileName}\" is larger than 5 MB.");
        var contentType = DetectContentType(fileName, content);
        return contentType is null
            ? (null, $"\"{fileName}\" is not a JPG, PNG or PDF file.")
            : (contentType, null);
    }
}
