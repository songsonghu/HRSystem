using HRSystem.Application.Common;
using HRSystem.Application.DTOs;

namespace HRSystem.Application.Interfaces;

/// <summary>
/// Account request workflow: onboarding (HR) and in-service add/remove raised by HR,
/// a department manager for their team, or an employee for themself (which then needs
/// the department manager's approval before it is dispatched to the responsible departments).
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

    /// <summary>Items dispatched to the current user.</summary>
    Task<IReadOnlyList<RequestItemDto>> GetMyPendingItemsAsync(CancellationToken ct = default);

    /// <summary>
    /// The assignee updates an item (open account, set status, remark). On Completed it
    /// writes to the employee account ledger; the master status is then re-evaluated.
    /// </summary>
    Task<Result> UpdateItemAsync(UpdateItemDto dto, CancellationToken ct = default);

    /// <summary>
    /// The account type checklist applicable to the given employee (Staff or AE/Sales/SA
    /// form), ordered as printed on the paper requisition forms.
    /// </summary>
    Task<IReadOnlyList<AccountTypeOptionDto>> GetAccountTypeOptionsAsync(int employeeId, CancellationToken ct = default);

    /// <summary>Attach a scanned, signed approval document to a request.</summary>
    Task<Result> AddAttachmentAsync(
        int requestId, Stream content, string fileName, string? contentType, CancellationToken ct = default);
}
