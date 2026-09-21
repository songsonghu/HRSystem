using System.ComponentModel.DataAnnotations;
using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Domain.Enums;

namespace HRSystem.Web.ViewModels;

public class DepartureCreateViewModel
{
    [Required]
    public int EmployeeId { get; set; }

    public string EmployeeNo { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Position { get; set; }
    public DateTime JoinDate { get; set; }

    [Display(Name = "Last Working Date")]
    [DataType(DataType.Date)]
    public DateTime LastWorkingDate { get; set; }

    [Display(Name = "Last Employment Date")]
    [DataType(DataType.Date)]
    public DateTime LastEmploymentDate { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }

    [StringLength(1000)]
    public string? Remark { get; set; }

    public IReadOnlyList<DepartureTemplatePreviewDto> TemplateDepartments { get; set; } = Array.Empty<DepartureTemplatePreviewDto>();
}

public class DepartureDetailsViewModel
{
    public DepartureRequestDto Request { get; set; } = new();
    public bool CanSubmit { get; set; }
    public bool CanFinalize { get; set; }
    public bool CanManage { get; set; }
}

public class DepartureTaskEditViewModel
{
    public int TaskId { get; set; }
    public int RequestId { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public string EmployeeNo { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public DepartureTaskStatus Status { get; set; }

    [StringLength(1000)]
    public string? TaskRemark { get; set; }
    public bool MarkAsCompleted { get; set; }
    public List<DepartureTaskItemEditViewModel> Items { get; set; } = new();
}

public class DepartureTaskItemEditViewModel
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsNotApplicable { get; set; }
    public string? Remark { get; set; }
}

public static class DepartureViewModelMapper
{
    public static DepartureCreateViewModel FromCreatePage(DepartureCreatePageDto dto) => new()
    {
        EmployeeId = dto.EmployeeId,
        EmployeeNo = dto.EmployeeNo,
        EmployeeName = dto.EmployeeName,
        Department = dto.Department,
        Position = dto.Position,
        JoinDate = dto.JoinDate,
        LastWorkingDate = dto.LastWorkingDateDefault,
        LastEmploymentDate = dto.LastEmploymentDateDefault,
        TemplateDepartments = dto.TemplateDepartments
    };

    public static CreateDepartureRequestDto ToCreateDto(this DepartureCreateViewModel vm) => new()
    {
        EmployeeId = vm.EmployeeId,
        LastWorkingDate = vm.LastWorkingDate,
        LastEmploymentDate = vm.LastEmploymentDate,
        Reason = vm.Reason,
        Remark = vm.Remark
    };

    public static DepartureTaskEditViewModel ToTaskEdit(this DepartureTaskDto dto) => new()
    {
        TaskId = dto.Id,
        RequestId = dto.DepartureRequestId,
        RequestNo = dto.RequestNo,
        EmployeeNo = dto.EmployeeNo,
        EmployeeName = dto.EmployeeName,
        DepartmentName = dto.DepartmentName,
        Status = dto.Status,
        TaskRemark = dto.TaskRemark,
        Items = dto.Items.Select(i => new DepartureTaskItemEditViewModel
        {
            Id = i.Id,
            Description = i.Description,
            IsRequired = i.IsRequired,
            IsCompleted = i.IsCompleted,
            IsNotApplicable = i.IsNotApplicable,
            Remark = i.Remark
        }).ToList()
    };

    public static DepartureTaskUpdateDto ToUpdateDto(this DepartureTaskEditViewModel vm) => new()
    {
        TaskId = vm.TaskId,
        TaskRemark = vm.TaskRemark,
        MarkAsCompleted = vm.MarkAsCompleted,
        Items = vm.Items.Select(i => new DepartureTaskItemUpdateDto
        {
            Id = i.Id,
            IsCompleted = i.IsCompleted,
            IsNotApplicable = i.IsNotApplicable,
            Remark = i.Remark
        }).ToList()
    };
}
