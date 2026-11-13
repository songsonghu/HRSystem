namespace HRSystem.Application.DTOs;

/// <summary>One department's handlers and approvers for account or departure work.</summary>
public class DepartmentAssignmentDto
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;

    /// <summary>The manager's login, used for a role nobody is set for.</summary>
    public string? ManagerUserName { get; set; }

    public List<string> HandlerUserIds { get; set; } = new();
    public List<string> ApproverUserIds { get; set; } = new();
}
