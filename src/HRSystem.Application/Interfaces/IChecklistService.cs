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

    /// <summary>
    /// Close a request whose required tasks are done. Departure marks the employee resigned.
    /// A departure with a final approver can only be finalized (approved) by that approver; otherwise HR finalizes.
    /// </summary>
    Task<Result> FinalizeAsync(int requestId, CancellationToken ct = default);

    /// <summary>Departures waiting for the current user's final approval.</summary>
    Task<IReadOnlyList<ChecklistRequestDto>> GetMyFinalReviewsAsync(CancellationToken ct = default);

    /// <summary>The current user's other final reviews (approved, cancelled, or back with departments).</summary>
    Task<IReadOnlyList<ChecklistRequestDto>> GetMyFinalReviewHistoryAsync(ChecklistTaskHistoryQuery query, CancellationToken ct = default);

    /// <summary>A departure with all task details, for its final approver only.</summary>
    Task<ChecklistRequestDto?> GetFinalReviewAsync(int requestId, CancellationToken ct = default);

    /// <summary>Final approver sends a finished department task back with a reason.</summary>
    Task<Result> ReturnTaskAsync(int taskId, string reason, CancellationToken ct = default);

    /// <summary>Withdraw a request that is not finalized; its open tasks and linked account request are closed.</summary>
    Task<Result> CancelAsync(int requestId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Open tasks of both kinds assigned to the current user. Tasks are only ever visible to
    /// their assignee — administrators included.
    /// </summary>
    Task<IReadOnlyList<ChecklistTaskDto>> GetMyPendingTasksAsync(CancellationToken ct = default);

    /// <summary>The current user's finished tasks, and tasks of requests that were finalized or cancelled.</summary>
    Task<IReadOnlyList<ChecklistTaskDto>> GetMyTaskHistoryAsync(ChecklistTaskHistoryQuery query, CancellationToken ct = default);

    Task<ChecklistTaskDto?> GetTaskAsync(int taskId, CancellationToken ct = default);
    Task<Result> UpdateTaskAsync(ChecklistTaskUpdateDto dto, CancellationToken ct = default);

    /// <summary>Take back a submitted task so it can be changed again (request not yet finalized).</summary>
    Task<Result> ReopenTaskAsync(int taskId, CancellationToken ct = default);

    /// <summary>A checklist item's attachment, for its assignee, the final approver and HR; null otherwise.</summary>
    Task<ChecklistAttachmentFile?> GetItemAttachmentAsync(int itemId, CancellationToken ct = default);
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

    /// <summary>Each template of this kind with the login that receives its department's task (needs checklist-approvers.manage).</summary>
    Task<IReadOnlyList<ChecklistApproverDto>> GetApproversAsync(ChecklistKind kind, CancellationToken ct = default);

    /// <summary>
    /// Set the approver login per template id (null/empty falls back to the department manager).
    /// Unfinished tasks of open requests move to the new assignee.
    /// </summary>
    Task<Result> SetApproversAsync(ChecklistKind kind, IReadOnlyDictionary<int, string?> approvers, CancellationToken ct = default);
}
