using HRSystem.Application.Common;

namespace HRSystem.Tests.Unit;

public class ChecklistAttachmentRulesTests
{
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 };
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };
    private static readonly byte[] Pdf = "%PDF-1.7\n"u8.ToArray();

    [Theory]
    [InlineData("scan.jpg", "image/jpeg")]
    [InlineData("SCAN.JPEG", "image/jpeg")]
    [InlineData("scan.png", "image/png")]
    [InlineData("scan.pdf", "application/pdf")]
    public void Images_and_pdfs_are_detected_from_their_content(string fileName, string expected)
    {
        var content = expected switch { "image/jpeg" => Jpeg, "image/png" => Png, _ => Pdf };
        Assert.Equal((expected, (string?)null), ChecklistAttachmentRules.Validate(fileName, content));
    }

    [Theory]
    [InlineData("scan.png", "jpeg")]   // extension does not match the content
    [InlineData("scan.pdf", "text")]
    [InlineData("scan.gif", "png")]    // type not allowed
    [InlineData("scan.exe", "pdf")]
    public void Other_files_are_rejected(string fileName, string content)
    {
        var bytes = content switch { "jpeg" => Jpeg, "png" => Png, "pdf" => Pdf, _ => "hello"u8.ToArray() };
        Assert.Contains("not a JPG, PNG or PDF", ChecklistAttachmentRules.Validate(fileName, bytes).Error);
    }

    [Fact]
    public void Empty_and_oversized_files_are_rejected()
    {
        Assert.Contains("empty", ChecklistAttachmentRules.Validate("a.pdf", Array.Empty<byte>()).Error);
        var big = new byte[ChecklistAttachmentRules.MaxBytes + 1];
        Pdf.CopyTo(big, 0);
        Assert.Contains("5 MB", ChecklistAttachmentRules.Validate("a.pdf", big).Error);
    }
}
