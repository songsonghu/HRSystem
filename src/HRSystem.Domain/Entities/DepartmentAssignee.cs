using HRSystem.Domain.Enums;

namespace HRSystem.Domain.Entities;

/// <summary>
/// A login that handles or approves one department's account or departure work. When a department
/// has nobody in a role, its manager's login takes that role.
/// </summary>
public class DepartmentAssignee
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public WorkArea Area { get; set; }
    public AssignmentRole Role { get; set; }
    public string UserId { get; set; } = string.Empty;

    public Department? Department { get; set; }
}
