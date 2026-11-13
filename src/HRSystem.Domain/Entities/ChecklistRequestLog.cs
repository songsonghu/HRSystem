using HRSystem.Domain.Enums;

namespace HRSystem.Domain.Entities;

/// <summary>One step of a checklist request's workflow: who did what, when, and their remark. Never changed after it is written.</summary>
public class ChecklistRequestLog
{
    public int Id { get; set; }
    public int ChecklistRequestId { get; set; }
    public ChecklistLogAction Action { get; set; }

    /// <summary>The department(s) the step concerns, e.g. the task approved or the departments returned to.</summary>
    public string? Departments { get; set; }

    public string? Remark { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;

    public ChecklistRequest? ChecklistRequest { get; set; }
}
