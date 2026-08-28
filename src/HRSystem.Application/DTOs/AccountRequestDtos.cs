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
}

/// <summary>Read model for a request (master).</summary>
public class RequestDto
{
    public int Id { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeNo { get; set; } = string.Empty;
    public RequestType RequestType { get; set; }
    public RequestStatus Status { get; set; }
    public string? Remark { get; set; }
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
