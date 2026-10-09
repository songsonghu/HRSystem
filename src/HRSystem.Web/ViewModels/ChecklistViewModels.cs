using System.ComponentModel.DataAnnotations;
using HRSystem.Application.DTOs;
using HRSystem.Domain.Enums;

namespace HRSystem.Web.ViewModels;

/// <summary>Create form for an onboarding or departure request.</summary>
public class ChecklistCreateViewModel
{
    public ChecklistKind Kind { get; set; }

    [Required]
    public int EmployeeId { get; set; }
    public string EmployeeNo { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Position { get; set; }
    public DateTime JoinDate { get; set; }

    [Display(Name = "Start Date")]
    [DataType(DataType.Date)]
    public DateTime? StartDate { get; set; }

    [Display(Name = "Last Working Date")]
    [DataType(DataType.Date)]
    public DateTime? LastWorkingDate { get; set; }

    [Display(Name = "Last Employment Date")]
    [DataType(DataType.Date)]
    public DateTime? LastEmploymentDate { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }

    [StringLength(1000)]
    public string? Remark { get; set; }

    public List<int> AccountTypeIds { get; set; } = new();
    public Dictionary<int, string> AccountDetails { get; set; } = new();

    public IReadOnlyList<ChecklistTemplatePreviewDto> Templates { get; set; } = Array.Empty<ChecklistTemplatePreviewDto>();
    public IReadOnlyList<AccountTypeOptionDto> AccountTypeOptions { get; set; } = Array.Empty<AccountTypeOptionDto>();
    public IReadOnlyDictionary<int, string?> ActiveAccounts { get; set; } = new Dictionary<int, string?>();
}

public class ChecklistTaskEditViewModel
{
    public int TaskId { get; set; }
    public int RequestId { get; set; }
    public ChecklistKind Kind { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public string EmployeeNo { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public ChecklistTaskStatus Status { get; set; }

    [StringLength(1000)]
    public string? TaskRemark { get; set; }
    public bool MarkAsCompleted { get; set; }
    public List<ChecklistTaskItemEditViewModel> Items { get; set; } = new();
}

public class ChecklistTaskItemEditViewModel
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsNotApplicable { get; set; }
    public string? Remark { get; set; }
}

public static class ChecklistViewModelMapper
{
    public static ChecklistCreateViewModel FromCreatePage(ChecklistCreatePageDto dto) => new()
    {
        Kind = dto.Kind,
        EmployeeId = dto.EmployeeId,
        EmployeeNo = dto.EmployeeNo,
        EmployeeName = dto.EmployeeName,
        Department = dto.Department,
        Position = dto.Position,
        JoinDate = dto.JoinDate,
        StartDate = dto.Kind == ChecklistKind.Onboarding ? dto.JoinDate : null,
        LastWorkingDate = dto.Kind == ChecklistKind.Departure ? DateTime.Today : null,
        LastEmploymentDate = dto.Kind == ChecklistKind.Departure ? DateTime.Today : null,
        Templates = dto.Templates,
        AccountTypeOptions = dto.AccountTypeOptions,
        ActiveAccounts = dto.ActiveAccounts,
        // Departure: every account the employee holds is ticked for disabling by default.
        AccountTypeIds = dto.ActiveAccounts.Keys.ToList()
    };

    /// <summary>Refresh the read-only parts of a posted form that is shown again.</summary>
    public static void CopyDisplayFrom(this ChecklistCreateViewModel vm, ChecklistCreatePageDto dto)
    {
        vm.EmployeeNo = dto.EmployeeNo;
        vm.EmployeeName = dto.EmployeeName;
        vm.Department = dto.Department;
        vm.Position = dto.Position;
        vm.JoinDate = dto.JoinDate;
        vm.Templates = dto.Templates;
        vm.AccountTypeOptions = dto.AccountTypeOptions;
        vm.ActiveAccounts = dto.ActiveAccounts;
    }

    public static CreateChecklistRequestDto ToCreateDto(this ChecklistCreateViewModel vm) => new()
    {
        Kind = vm.Kind,
        EmployeeId = vm.EmployeeId,
        StartDate = vm.StartDate,
        LastWorkingDate = vm.LastWorkingDate,
        LastEmploymentDate = vm.LastEmploymentDate,
        Reason = vm.Reason,
        Remark = vm.Remark,
        AccountTypeIds = vm.AccountTypeIds,
        AccountDetails = vm.AccountDetails
            .Where(kv => vm.AccountTypeIds.Contains(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value)
    };

    public static ChecklistTaskEditViewModel ToTaskEdit(this ChecklistTaskDto dto) => new()
    {
        TaskId = dto.Id,
        RequestId = dto.ChecklistRequestId,
        Kind = dto.Kind,
        RequestNo = dto.RequestNo,
        EmployeeNo = dto.EmployeeNo,
        EmployeeName = dto.EmployeeName,
        DepartmentName = dto.DepartmentName,
        Status = dto.Status,
        TaskRemark = dto.TaskRemark,
        Items = dto.Items.Select(i => new ChecklistTaskItemEditViewModel
        {
            Id = i.Id,
            Description = i.Description,
            IsRequired = i.IsRequired,
            IsCompleted = i.IsCompleted,
            IsNotApplicable = i.IsNotApplicable,
            Remark = i.Remark
        }).ToList()
    };

    public static ChecklistTaskUpdateDto ToUpdateDto(this ChecklistTaskEditViewModel vm) => new()
    {
        TaskId = vm.TaskId,
        TaskRemark = vm.TaskRemark,
        MarkAsCompleted = vm.MarkAsCompleted,
        Items = vm.Items.Select(i => new ChecklistTaskItemUpdateDto
        {
            Id = i.Id,
            IsCompleted = i.IsCompleted,
            IsNotApplicable = i.IsNotApplicable,
            Remark = i.Remark
        }).ToList()
    };
}
