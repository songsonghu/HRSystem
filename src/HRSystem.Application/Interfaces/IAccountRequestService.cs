using HRSystem.Application.Common;
using HRSystem.Application.DTOs;

namespace HRSystem.Application.Interfaces;

/// <summary>
/// Account provisioning workflow use cases (Modules 3 &amp; 4).
/// Handles onboarding, in-service add/remove, submission, dispatch, and
/// department-side item completion.
/// </summary>
public interface IAccountRequestService
{
    /// <summary>Create a request in Draft state and fan out into items.</summary>
    Task<Result<int>> CreateAsync(CreateRequestDto dto, CancellationToken ct = default);

    /// <summary>
    /// Submit the request: validates, dispatches items to responsible
    /// departments, and triggers the new-employee + department emails.
    /// </summary>
    Task<Result> SubmitAsync(int requestId, CancellationToken ct = default);

    /// <summary>Get a single request with its items and attachments.</summary>
    Task<RequestDto?> GetAsync(int requestId, CancellationToken ct = default);

    /// <summary>Admin view: all requests (optionally filtered).</summary>
    Task<IReadOnlyList<RequestDto>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Department head view: items assigned to the current user's dept.</summary>
    Task<IReadOnlyList<RequestItemDto>> GetMyPendingItemsAsync(CancellationToken ct = default);

    /// <summary>
    /// Department head updates an item (open account, set status, remark).
    /// On Completed it writes to the employee account ledger and re-evaluates
    /// the master request status.
    /// </summary>
    Task<Result> UpdateItemAsync(UpdateItemDto dto, CancellationToken ct = default);

    /// <summary>
    /// Get the account type checklist applicable to the given employee
    /// (Staff or AE/Sales/SA form), grouped by responsible department and
    /// ordered as printed on the paper requisition forms.
    /// </summary>
    Task<IReadOnlyList<AccountTypeOptionDto>> GetAccountTypeOptionsAsync(int employeeId, CancellationToken ct = default);

    /// <summary>Attach a scanned, signed approval document to a request.</summary>
    Task<Result> AddAttachmentAsync(
        int requestId, Stream content, string fileName, string? contentType, CancellationToken ct = default);
}
