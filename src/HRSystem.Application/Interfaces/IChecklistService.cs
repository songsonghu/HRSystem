using HRSystem.Application.Common;
using HRSystem.Application.DTOs;
using HRSystem.Domain.Enums;

namespace HRSystem.Application.Interfaces;

/// <summary>
/// Onboarding and departure checklists. Managing requests of a kind needs that kind's
/// permission (onboarding.manage / departures.manage). Onboarding only opens accounts; a departure
/// gives each department a task that its handlers complete and its approvers approve.
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

    /// <summary>Submit the linked account request and, for a departure, dispatch the department tasks.</summary>
    Task<Result> SubmitAsync(int requestId, CancellationToken ct = default);

    /// <summary>HR closes an onboarding request once its accounts are open. Departures complete through their approvals instead.</summary>
    Task<Result> FinalizeAsync(int requestId, CancellationToken ct = default);

    /// <summary>Departures waiting for the current user's approval as the leaver's department head.</summary>
    Task<IReadOnlyList<ChecklistRequestDto>> GetMyFinalReviewsAsync(CancellationToken ct = default);

    /// <summary>Departures waiting for HR / management approval, for holders of departures.approve (their own departure excluded).</summary>
    Task<IReadOnlyList<ChecklistRequestDto>> GetMyManagementApprovalsAsync(CancellationToken ct = default);

    /// <summary>Departures the current user heads or decided on as HR / management that are not waiting for them now.</summary>
    Task<IReadOnlyList<ChecklistRequestDto>> GetMyFinalReviewHistoryAsync(ChecklistTaskHistoryQuery query, CancellationToken ct = default);

    /// <summary>A departure with all task details and its log, for its department head and HR / management approvers.</summary>
    Task<ChecklistRequestDto?> GetFinalReviewAsync(int requestId, CancellationToken ct = default);

    /// <summary>The department head approves with a remark; the departure goes to HR / management.</summary>
    Task<Result> ApproveByHeadAsync(int requestId, string remark, CancellationToken ct = default);

    /// <summary>HR / management approve with a remark: the departure is completed and the employee marked resigned.</summary>
    Task<Result> ApproveByManagementAsync(int requestId, string remark, CancellationToken ct = default);

    /// <summary>HR / management send the departure back to the department head with a reason.</summary>
    Task<Result> ReturnToHeadAsync(int requestId, string reason, CancellationToken ct = default);

    /// <summary>
    /// The department head (or HR / management when the leaver heads their own department) sends the
    /// departure back: the chosen departments redo their tasks, which their approvers approve again.
    /// </summary>
    Task<Result> ReturnTasksAsync(int requestId, IReadOnlyCollection<int> taskIds, string reason, CancellationToken ct = default);

    /// <summary>Withdraw a request that is not finalized; its open tasks and linked account request are closed.</summary>
    Task<Result> CancelAsync(int requestId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Unfinished tasks of the departments the current user handles. Tasks are only visible to their
    /// department's handlers and approvers — administrators included.
    /// </summary>
    Task<IReadOnlyList<ChecklistTaskDto>> GetMyPendingTasksAsync(CancellationToken ct = default);

    /// <summary>Finished tasks of the departments the current user handles, and tasks of closed requests.</summary>
    Task<IReadOnlyList<ChecklistTaskDto>> GetMyTaskHistoryAsync(ChecklistTaskHistoryQuery query, CancellationToken ct = default);

    /// <summary>Tasks of open requests not yet approved, in the departments the current user approves.</summary>
    Task<IReadOnlyList<ChecklistTaskDto>> GetMyApprovalTasksAsync(CancellationToken ct = default);

    /// <summary>Approved tasks, and tasks of closed requests, in the departments the current user approves.</summary>
    Task<IReadOnlyList<ChecklistTaskDto>> GetMyApprovalHistoryAsync(ChecklistTaskHistoryQuery query, CancellationToken ct = default);

    /// <summary>The department approver accepts a completed task, with an optional remark.</summary>
    Task<Result> ApproveTaskAsync(int taskId, string? remark = null, CancellationToken ct = default);

    /// <summary>The department approver sends a completed task back to the handlers with a reason.</summary>
    Task<Result> ReturnTaskToHandlersAsync(int taskId, string reason, CancellationToken ct = default);

    Task<ChecklistTaskDto?> GetTaskAsync(int taskId, CancellationToken ct = default);
    /// <summary>
    /// Save or complete a task. A completed task is locked for its handlers; only its department
    /// approver (or the final approver) can send it back for changes.
    /// </summary>
    Task<Result> UpdateTaskAsync(ChecklistTaskUpdateDto dto, CancellationToken ct = default);

    /// <summary>A checklist item's attachment, for its department's handlers and approvers, the final approver and HR; null otherwise.</summary>
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
}
