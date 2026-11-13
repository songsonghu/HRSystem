using HRSystem.Application.Common;
using HRSystem.Application.DTOs;
using HRSystem.Domain.Enums;

namespace HRSystem.Application.Interfaces;

/// <summary>
/// Who handles and who approves each department's account and departure work. Several logins may
/// share a role; a department with nobody in a role falls back to its manager's login.
/// </summary>
public interface IAssignmentService
{
    /// <summary>Departments in which the user holds the role.</summary>
    Task<IReadOnlyList<int>> GetDepartmentIdsAsync(string? userId, WorkArea area, AssignmentRole role, CancellationToken ct = default);

    /// <summary>Whether the user holds the role in the department.</summary>
    Task<bool> HoldsAsync(string? userId, int? departmentId, WorkArea area, AssignmentRole role, CancellationToken ct = default);

    /// <summary>The logins holding the role in each given department; empty when there are none.</summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<UserDirectoryEntry>>> GetAssigneesAsync(
        IEnumerable<int> departmentIds, WorkArea area, AssignmentRole role, CancellationToken ct = default);

    /// <summary>
    /// The departments among the given ones that lack a handler or an approver, so work for them
    /// cannot be dispatched; null when all are covered.
    /// </summary>
    Task<string?> CheckCoverageAsync(IEnumerable<int> departmentIds, WorkArea area, CancellationToken ct = default);

    /// <summary>One department's handlers and approvers, for the template pages; null when not allowed or not found.</summary>
    Task<DepartmentAssignmentDto?> GetDepartmentAsync(int departmentId, WorkArea area, CancellationToken ct = default);

    /// <summary>Replace the handlers and approvers of the given departments (needs checklist-approvers.manage).</summary>
    Task<Result> SaveSettingsAsync(WorkArea area, IReadOnlyList<DepartmentAssignmentDto> rows, CancellationToken ct = default);
}
