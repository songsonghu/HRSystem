using HRSystem.Domain.Enums;

namespace HRSystem.Application.DTOs;

/// <summary>Input for a new onboarding or departure draft. Kind-specific fields are ignored for the other kind.</summary>
public class CreateChecklistRequestDto
{
    public ChecklistKind Kind { get; set; }
    public int EmployeeId { get; set; }

    // Onboarding
    public DateTime? StartDate { get; set; }
    public List<int> AccountTypeIds { get; set; } = new();
    public Dictionary<int, string> AccountDetails { get; set; } = new();

    // Departure
    public DateTime? LastWorkingDate { get; set; }
    public DateTime? LastEmploymentDate { get; set; }
    public string? Reason { get; set; }

    public string? Remark { get; set; }
}

public class ChecklistRequestDto
{
    public int Id { get; set; }
    public ChecklistKind Kind { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public int EmployeeId { get; set; }
    public string EmployeeNo { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public ChecklistStatus Status { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? LastWorkingDate { get; set; }
    public DateTime? LastEmploymentDate { get; set; }
    public string? Reason { get; set; }
    public string? Remark { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }

    /// <summary>Departure: the leaver's department head who gives the final approval.</summary>
    public string? FinalApproverUserId { get; set; }
    public string? FinalApproverName { get; set; }

    public int? AccountRequestId { get; set; }
    public string? AccountRequestNo { get; set; }
    public RequestStatus? AccountRequestStatus { get; set; }

    public List<ChecklistTaskDto> Tasks { get; set; } = new();

    public bool CanSubmit => Status == ChecklistStatus.Draft;
    /// <summary>
    /// Onboarding has no department tasks: HR finalizes it once its accounts are open. A departure
    /// waits until every required department task is approved.
    /// </summary>
    public bool CanFinalize => Kind == ChecklistKind.Onboarding
        ? Status is ChecklistStatus.Submitted or ChecklistStatus.InProgress or ChecklistStatus.PendingFinalReview
        : Status == ChecklistStatus.PendingFinalReview;

    /// <summary>
    /// HR finalizes onboarding, and departures whose leaver heads their own department (or that
    /// were submitted before final approval existed). Otherwise the department head approves.
    /// </summary>
    public bool FinalizedByHr => Kind == ChecklistKind.Onboarding || FinalApproverUserId is null;
    public bool CanCancel => Status is not (ChecklistStatus.Completed or ChecklistStatus.Cancelled);
}

public class ChecklistTaskDto
{
    public int Id { get; set; }
    public int ChecklistRequestId { get; set; }
    public ChecklistKind Kind { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public string EmployeeNo { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public ChecklistTaskStatus Status { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public string? TaskRemark { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ReturnReason { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public ChecklistStatus RequestStatus { get; set; }
    public List<ChecklistTaskItemDto> Items { get; set; } = new();

    public bool IsDone => Status is ChecklistTaskStatus.Completed or ChecklistTaskStatus.NotApplicable;
    public bool IsApproved => ApprovedAt is not null;
    public bool IsRequestOpen => RequestStatus is not (ChecklistStatus.Draft or ChecklistStatus.Completed or ChecklistStatus.Cancelled);

    // What the current user may do, as a handler or an approver of the task's department.
    public bool IsHandler { get; set; }
    public bool IsApprover { get; set; }

    /// <summary>Handlers and approvers may change the items until the task is completed.</summary>
    public bool CanEdit => IsRequestOpen && !IsDone && (IsHandler || IsApprover);

    /// <summary>A completed task can be taken back until the department approver approves it.</summary>
    public bool CanReopen => IsRequestOpen && IsDone && !IsApproved && (IsHandler || IsApprover);

    /// <summary>The department approver approves a completed task, or returns it to the handlers.</summary>
    public bool CanApprove => IsRequestOpen && IsDone && !IsApproved && IsApprover;
}

/// <summary>Filters for the current user's checklist task history.</summary>
public class ChecklistTaskHistoryQuery
{
    /// <summary>Matches employee name, employee number or request number.</summary>
    public string? Keyword { get; set; }
    public ChecklistKind? Kind { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class ChecklistTaskItemDto
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsNotApplicable { get; set; }
    public string? Remark { get; set; }
    public bool AllowAttachment { get; set; }
    public string? AttachmentFileName { get; set; }
    public long? AttachmentSize { get; set; }
    public bool HasAttachment => AttachmentFileName is not null;
}

/// <summary>A file chosen for a checklist item; the content type is detected from the content.</summary>
public record ChecklistAttachmentUpload(string FileName, byte[] Content);

/// <summary>A stored checklist item attachment, ready to download.</summary>
public record ChecklistAttachmentFile(string FileName, string ContentType, byte[] Content);

public class ChecklistTaskUpdateDto
{
    public int TaskId { get; set; }
    public string? TaskRemark { get; set; }
    public bool MarkAsCompleted { get; set; }
    public List<ChecklistTaskItemUpdateDto> Items { get; set; } = new();
}

public class ChecklistTaskItemUpdateDto
{
    public int Id { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsNotApplicable { get; set; }
    public string? Remark { get; set; }

    /// <summary>Replaces the current attachment; ignored unless the item allows attachments.</summary>
    public ChecklistAttachmentUpload? NewAttachment { get; set; }
    public bool RemoveAttachment { get; set; }
}

/// <summary>What the create page shows for the chosen employee.</summary>
public class ChecklistCreatePageDto
{
    public ChecklistKind Kind { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeNo { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Position { get; set; }
    public DateTime JoinDate { get; set; }
    public IReadOnlyList<ChecklistTemplatePreviewDto> Templates { get; set; } = Array.Empty<ChecklistTemplatePreviewDto>();

    /// <summary>
    /// Onboarding: account types to open. Departure: account types to disable — the employee's
    /// form plus any account they currently hold.
    /// </summary>
    public IReadOnlyList<AccountTypeOptionDto> AccountTypeOptions { get; set; } = Array.Empty<AccountTypeOptionDto>();

    /// <summary>Departure: the employee's active accounts (account type id -> account value), ticked by default.</summary>
    public IReadOnlyDictionary<int, string?> ActiveAccounts { get; set; } = new Dictionary<int, string?>();

    /// <summary>Departure: who gives the final approval; null when HR finalizes (leaver heads the department).</summary>
    public string? FinalApproverName { get; set; }

    /// <summary>Departure: why no final approver can be found, so submitting would fail.</summary>
    public string? FinalApprovalError { get; set; }
}

public class ChecklistTemplatePreviewDto
{
    public string DepartmentName { get; set; } = string.Empty;
    public int ItemCount { get; set; }

    /// <summary>Who processes and who approves the task; null when nobody, so submitting would fail.</summary>
    public string? HandlerNames { get; set; }
    public string? ApproverNames { get; set; }
    public bool CanReceiveTasks => HandlerNames is not null && ApproverNames is not null;
}

// ------------------------------------------------------------- templates

public class ChecklistTemplateListItemDto
{
    public int Id { get; set; }
    public ChecklistKind Kind { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public int ItemCount { get; set; }
    public int DepartmentId { get; set; }
    public string? HandlerNames { get; set; }
    public string? ApproverNames { get; set; }
    public bool CanReceiveTasks => HandlerNames is not null && ApproverNames is not null;
}

public class ChecklistTemplateEditDto
{
    public int Id { get; set; }
    public ChecklistKind Kind { get; set; }
    public int? DepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsRequired { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>Rows with an empty description are ignored, so the form can offer blank rows to add items.</summary>
    public List<ChecklistTemplateItemEditDto> Items { get; set; } = new();
}

public class ChecklistTemplateItemEditDto
{
    public int Id { get; set; }
    public string? Description { get; set; }
    public bool IsRequired { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>Departure only: show an upload button for this item on the task page.</summary>
    public bool AllowAttachment { get; set; }
}

