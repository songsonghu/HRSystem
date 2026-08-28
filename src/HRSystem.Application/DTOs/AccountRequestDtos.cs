using HRSystem.Domain.Enums;

namespace HRSystem.Application.DTOs;

/// <summary>Input model to create an account request (onboard / add / remove).</summary>
public class CreateRequestDto
{
    public int EmployeeId { get; set; }
    public RequestType RequestType { get; set; } = RequestType.Onboard;
    public string? Remark { get; set; }

    /// <summary>Selected account type ids to provision or remove.</summary>
    public List<int> AccountTypeIds { get; set; } = new();

    /// <summary>
    /// Optional free-text detail keyed by account type id, for items that
    /// require it (e.g. E-mail "Group(s)/Sub-group(s)", E-report/FXES
    /// "Access profile", the SFC license number).
    /// </summary>
    public Dictionary<int, string> Details { get; set; } = new();

    /// <summary>
    /// Staff form only: true for a new headcount, false for a replacement.
    /// Left null for in-service add/remove and offboard requests.
    /// </summary>
    public bool? IsNewHeadcount { get; set; }

    /// <summary>Staff form only: name of the employee being replaced.</summary>
    public string? ReplacementOf { get; set; }

    /// <summary>Staff form only: last working day of the employee being replaced.</summary>
    public DateTime? LastDay { get; set; }
}

/// <summary>Read model for a request (master).</summary>
public class RequestDto
{
    public int Id { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeNo { get; set; } = string.Empty;
    public EmployeeCategory EmployeeCategory { get; set; }
    public RequestType RequestType { get; set; }
    public RequestStatus Status { get; set; }
    public string? Remark { get; set; }
    public bool? IsNewHeadcount { get; set; }
    public string? ReplacementOf { get; set; }
    public DateTime? LastDay { get; set; }
    public DateTime? AppliedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<RequestItemDto> Items { get; set; } = new();
    public List<AttachmentDto> Attachments { get; set; } = new();
}

/// <summary>Read model for a request item (one account type / department).</summary>
public class RequestItemDto
{
    public int Id { get; set; }
    public int AccountTypeId { get; set; }
    public string AccountTypeName { get; set; } = string.Empty;
    public int AssignedDeptId { get; set; }
    public string AssignedDeptName { get; set; } = string.Empty;
    public ItemStatus Status { get; set; }
    public string? AccountValue { get; set; }
    public string? RequestDetail { get; set; }
    public string? ResultRemark { get; set; }
    public DateTime? HandledAt { get; set; }
}

/// <summary>Input model used by a department head to update an item.</summary>
public class UpdateItemDto
{
    public int ItemId { get; set; }
    public ItemStatus Status { get; set; }
    public string? AccountValue { get; set; }
    public string? ResultRemark { get; set; }
}

/// <summary>Read model for an attachment.</summary>
public class AttachmentDto
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
}

/// <summary>
/// Option shown on the New Account Request checklist: one selectable
/// account type, grouped by responsible department, mirroring the layout
/// of the paper "Staff Requisition Form" / "AE-Sales-SA Requisition Form".
/// </summary>
public class AccountTypeOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DeptId { get; set; }
    public string DeptName { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool RequiresDetail { get; set; }
    public string? DetailLabel { get; set; }
}
