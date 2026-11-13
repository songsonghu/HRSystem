using HRSystem.Domain.Common;

namespace HRSystem.Domain.Entities;

public class ChecklistTaskItem : BaseEntity
{
    public int ChecklistTaskId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; } = true;
    public bool IsCompleted { get; set; }
    public bool IsNotApplicable { get; set; }
    public string? Remark { get; set; }
    public string? CompletedBy { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Copied from the template item: whether an attachment may be uploaded.</summary>
    public bool AllowAttachment { get; set; }
    public string? AttachmentFileName { get; set; }
    public string? AttachmentContentType { get; set; }
    public long? AttachmentSize { get; set; }

    public ChecklistTask? ChecklistTask { get; set; }

    /// <summary>
    /// The attached file. Its content is a column of this item's row, mapped separately so that
    /// loading items never loads file content. Null when nothing is attached.
    /// </summary>
    public ChecklistTaskItemAttachment? Attachment { get; set; }
}

/// <summary>The <c>AttachmentContent</c> column of <see cref="ChecklistTaskItem"/>, loaded only on demand.</summary>
public class ChecklistTaskItemAttachment
{
    public int Id { get; set; }
    public byte[] Content { get; set; } = Array.Empty<byte>();
}
