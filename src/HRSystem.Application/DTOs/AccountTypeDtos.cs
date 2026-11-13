using HRSystem.Domain.Enums;

namespace HRSystem.Application.DTOs;

/// <summary>A department on the Templates for Accounts list.</summary>
public class AccountTemplateListItemDto
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public bool DepartmentIsActive { get; set; }
    public int ActiveCount { get; set; }
    public int InactiveCount { get; set; }
    public string? HandlerNames { get; set; }
    public string? ApproverNames { get; set; }
}

/// <summary>The account types one department opens and disables.</summary>
public class AccountTemplateEditDto
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;

    /// <summary>Rows without an id and with an empty name are ignored, so the form can offer blank rows.</summary>
    public List<AccountTypeRowDto> Rows { get; set; } = new();
}

public class AccountTypeRowDto
{
    public int Id { get; set; }
    public string? Name { get; set; }

    /// <summary>Unique code; generated from the name when left empty.</summary>
    public string? Code { get; set; }
    public string? Description { get; set; }
    public AccountTypeAudience Audience { get; set; } = AccountTypeAudience.Both;
    public bool RequiresDetail { get; set; }
    public string? DetailLabel { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
