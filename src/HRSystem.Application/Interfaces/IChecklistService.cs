using HRSystem.Application.Common;
using HRSystem.Application.DTOs;
using HRSystem.Domain.Enums;

namespace HRSystem.Application.Interfaces;

/// <summary>
/// Onboarding and departure checklists. Managing requests of a kind needs that kind's
/// permission (onboarding.manage / departures.manage); tasks are handled by their assignee.
/// </summary>
public interface IChecklistService
{
    Task<IReadOnlyList<ChecklistRequestDto>> GetListAsync(ChecklistKind kind, CancellationToken ct = default);
    Task<int?> GetOpenRequestIdAsync(ChecklistKind kind, int employeeId, CancellationToken ct = default);

    /// <summary>Active employees who can start a request of this kind.</summary>
    Task<IReadOnlyList<EmployeeOptionDto>> GetEligibleEmployeesAsync(ChecklistKind kind, CancellationToken ct = default);

    Task<Result<ChecklistCreatePageDto>> GetCreatePageAsync(ChecklistKind kind, int employeeId, CancellationToken ct = default);
    Task<Result<int>> CreateDraftAsync(CreateChecklistRequestDto dto, CancellationToken ct = default);

    /// <summary>A request, or null when it does not exist or the user may not manage its kind.</summary>
    Task<ChecklistRequestDto?> GetAsync(int requestId, CancellationToken ct = default);

    /// <summary>Dispatch department tasks (and, for onboarding, submit the linked account request).</summary>
    Task<Result> SubmitAsync(int requestId, CancellationToken ct = default);

    /// <summary>Close a request whose required tasks are done. Departure marks the employee resigned.</summary>
    Task<Result> FinalizeAsync(int requestId, CancellationToken ct = default);

    /// <summary>Open tasks of both kinds assigned to the current user.</summary>
    Task<IReadOnlyList<ChecklistTaskDto>> GetMyPendingTasksAsync(CancellationToken ct = default);
    Task<ChecklistTaskDto?> GetTaskAsync(int taskId, CancellationToken ct = default);
    Task<Result> UpdateTaskAsync(ChecklistTaskUpdateDto dto, CancellationToken ct = default);
}

/// <summary>Per-department checklist templates, editable by holders of the kind's permission.</summary>
public interface IChecklistTemplateService
{
    Task<IReadOnlyList<ChecklistTemplateListItemDto>> GetListAsync(ChecklistKind kind, CancellationToken ct = default);
    Task<ChecklistTemplateEditDto?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Active departments without a template of this kind, plus <paramref name="includeDepartmentId"/>.</summary>
    Task<IReadOnlyList<DepartmentOptionDto>> GetDepartmentOptionsAsync(ChecklistKind kind, int? includeDepartmentId, CancellationToken ct = default);

    Task<Result<int>> CreateAsync(ChecklistTemplateEditDto dto, CancellationToken ct = default);
    Task<Result> UpdateAsync(ChecklistTemplateEditDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
}
