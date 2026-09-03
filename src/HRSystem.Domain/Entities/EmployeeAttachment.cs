using HRSystem.Domain.Common;

namespace HRSystem.Domain.Entities;

/// <summary>A document attached to an employee record.</summary>
public class EmployeeAttachment : BaseEntity
{
    public int EmployeeId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long FileSize { get; set; }

    public Employee? Employee { get; set; }
}
