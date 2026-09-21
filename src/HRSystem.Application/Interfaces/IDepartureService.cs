using HRSystem.Application.Common;
using HRSystem.Application.DTOs;

namespace HRSystem.Application.Interfaces;

public interface IDepartureService
{
    Task<Result<DepartureCreatePageDto>> GetCreatePageAsync(int employeeId, CancellationToken ct = default);
    Task<Result<int>> CreateDraftAsync(CreateDepartureRequestDto dto, CancellationToken ct = default);
    Task<DepartureRequestDto?> GetAsync(int requestId, CancellationToken ct = default);
    Task<Result> SubmitAsync(int requestId, CancellationToken ct = default);
    Task<IReadOnlyList<DepartureTaskDto>> GetMyPendingTasksAsync(CancellationToken ct = default);
    Task<DepartureTaskDto?> GetTaskAsync(int taskId, CancellationToken ct = default);
    Task<Result> UpdateTaskAsync(DepartureTaskUpdateDto dto, CancellationToken ct = default);
    Task<Result> FinalizeAsync(int requestId, CancellationToken ct = default);
}

public class DepartureCreatePageDto
{
    public int EmployeeId { get; set; }
    public string EmployeeNo { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Position { get; set; }
    public DateTime JoinDate { get; set; }
    public DateTime LastWorkingDateDefault { get; set; }
    public DateTime LastEmploymentDateDefault { get; set; }
    public IReadOnlyList<DepartureTemplatePreviewDto> TemplateDepartments { get; set; } = Array.Empty<DepartureTemplatePreviewDto>();
}

public class DepartureTemplatePreviewDto
{
    public string DepartmentName { get; set; } = string.Empty;
    public int ItemCount { get; set; }
}
