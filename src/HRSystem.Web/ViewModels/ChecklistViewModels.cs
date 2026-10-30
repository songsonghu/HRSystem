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
    public string? FinalApproverName { get; set; }
    public string? FinalApprovalError { get; set; }
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
    public DateTime? CompletedAt { get; set; }
    public string? ReturnReason { get; set; }
    public bool CanEdit { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public string? ReturnedByName { get; set; }
    public bool CanApprove { get; set; }
    public bool IsDone { get; set; }
    public bool IsRequestOpen { get; set; }
    public bool IsHandler { get; set; }
    public bool IsApprover { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTime? ApprovedAt { get; set; }

    [StringLength(1000)]
    public string? TaskRemark { get; set; }

    /// <summary>Set by the "Complete Task" button; "Save Progress" leaves it false.</summary>
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

    public bool AllowAttachment { get; set; }
    public string? AttachmentFileName { get; set; }
    public long? AttachmentSize { get; set; }

    /// <summary>A newly chosen file; replaces the current attachment.</summary>
    public IFormFile? Attachment { get; set; }
    public bool RemoveAttachment { get; set; }
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
        FinalApproverName = dto.FinalApproverName,
        FinalApprovalError = dto.FinalApprovalError,
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
        vm.FinalApproverName = dto.FinalApproverName;
        vm.FinalApprovalError = dto.FinalApprovalError;
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
        CompletedAt = dto.CompletedAt,
        ReturnReason = dto.ReturnReason,
        CanEdit = dto.CanEdit,
        ReturnedAt = dto.ReturnedAt,
        ReturnedByName = dto.ReturnedByName,
        CanApprove = dto.CanApprove,
        IsDone = dto.IsDone,
        IsRequestOpen = dto.IsRequestOpen,
        IsHandler = dto.IsHandler,
        IsApprover = dto.IsApprover,
        ApprovedByName = dto.ApprovedByName,
        ApprovedAt = dto.ApprovedAt,
        TaskRemark = dto.TaskRemark,
        Items = dto.Items.Select(i => new ChecklistTaskItemEditViewModel
        {
            Id = i.Id,
            Description = i.Description,
            IsRequired = i.IsRequired,
            IsCompleted = i.IsCompleted,
            IsNotApplicable = i.IsNotApplicable,
            Remark = i.Remark,
            AllowAttachment = i.AllowAttachment,
            AttachmentFileName = i.AttachmentFileName,
            AttachmentSize = i.AttachmentSize
        }).ToList()
    };

    /// <summary>Reads the chosen files; the caller has already rejected files over the size limit.</summary>
    public static async Task<ChecklistTaskUpdateDto> ToUpdateDtoAsync(this ChecklistTaskEditViewModel vm, CancellationToken ct = default)
    {
        var dto = new ChecklistTaskUpdateDto
        {
            TaskId = vm.TaskId,
            TaskRemark = vm.TaskRemark,
            MarkAsCompleted = vm.MarkAsCompleted
        };
        foreach (var i in vm.Items)
        {
            ChecklistAttachmentUpload? upload = null;
            if (i.Attachment is { Length: > 0 } file)
            {
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, ct);
                upload = new ChecklistAttachmentUpload(file.FileName, buffer.ToArray());
            }
            dto.Items.Add(new ChecklistTaskItemUpdateDto
            {
                Id = i.Id,
                IsCompleted = i.IsCompleted,
                IsNotApplicable = i.IsNotApplicable,
                Remark = i.Remark,
                NewAttachment = upload,
                RemoveAttachment = i.RemoveAttachment
            });
        }
        return dto;
    }
}
