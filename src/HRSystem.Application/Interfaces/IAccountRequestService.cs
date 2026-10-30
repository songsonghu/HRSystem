using HRSystem.Application.Common;
using HRSystem.Application.DTOs;

namespace HRSystem.Application.Interfaces;

/// <summary>
/// Account request workflow: onboarding (HR) and in-service add/remove raised by HR,
/// a department manager for their team, or an employee for themself (which then needs
/// the department manager's approval before it is dispatched to the responsible departments).
/// Each department's handlers process its items and its approvers approve them.
/// All operations apply the current user's access rules.
/// </summary>
public interface IAccountRequestService
{
    /// <summary>Employees the current user may raise requests for, and the allowed request types.</summary>
    Task<RequestCreateContextDto> GetCreateContextAsync(CancellationToken ct = default);

    /// <summary>Create a request in Draft state and fan out into items.</summary>
    Task<Result<int>> CreateAsync(CreateRequestDto dto, CancellationToken ct = default);

    /// <summary>
    /// Submit a draft. Self-service requests go to the employee's manager for approval;
    /// all others are dispatched to the responsible departments immediately.
    /// </summary>
    Task<Result> SubmitAsync(int requestId, CancellationToken ct = default);

    Task<Result> ApproveAsync(int requestId, CancellationToken ct = default);
    Task<Result> RejectAsync(int requestId, string reason, CancellationToken ct = default);

    /// <summary>A single request, or null when it does not exist or the user may not see it.</summary>
    Task<RequestDto?> GetAsync(int requestId, CancellationToken ct = default);

    /// <summary>Requests visible to the current user (all of them for HR).</summary>
    Task<IReadOnlyList<RequestDto>> GetListAsync(CancellationToken ct = default);

    /// <summary>Self-service requests waiting for the current user's approval.</summary>
    Task<IReadOnlyList<RequestDto>> GetPendingApprovalsAsync(CancellationToken ct = default);

    /// <summary>Unfinished items of the departments the current user handles.</summary>
    Task<IReadOnlyList<RequestItemDto>> GetMyPendingItemsAsync(CancellationToken ct = default);

    /// <summary>Items of dispatched requests not yet approved, in the departments the current user approves.</summary>
    Task<IReadOnlyList<RequestItemDto>> GetMyApprovalItemsAsync(CancellationToken ct = default);

    /// <summary>
    /// A handler or approver of the item's department updates it (open account, set status, remark).
    /// Completed or Rejected submits it to the department approvers.
    /// </summary>
    Task<Result> UpdateItemAsync(UpdateItemDto dto, CancellationToken ct = default);

    /// <summary>
    /// A department approver accepts a completed or rejected item. A completed item then updates the
    /// employee account ledger, and the request status is re-evaluated.
    /// </summary>
    Task<Result> ApproveItemAsync(int itemId, CancellationToken ct = default);

    /// <summary>A department approver sends a completed or rejected item back to the handlers with a reason.</summary>
    Task<Result> ReturnItemAsync(int itemId, string reason, CancellationToken ct = default);

    /// <summary>
    /// The account type checklist applicable to the given employee (Staff or AE/Sales/SA
    /// form), ordered as printed on the paper requisition forms.
    /// </summary>
    Task<IReadOnlyList<AccountTypeOptionDto>> GetAccountTypeOptionsAsync(int employeeId, CancellationToken ct = default);

    /// <summary>Attach a scanned, signed approval document to a request.</summary>
    Task<Result> AddAttachmentAsync(
        int requestId, Stream content, string fileName, string? contentType, CancellationToken ct = default);
}
