using HRSystem.Application.Common;
using HRSystem.Application.DTOs;

namespace HRSystem.Application.Interfaces;

/// <summary>Organization structure: departments and their managers.</summary>
public interface IDepartmentService
{
    Task<IReadOnlyList<DepartmentListItemDto>> GetListAsync(CancellationToken ct = default);
    Task<DepartmentDetailsDto?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Active employees that can be chosen as a manager.</summary>
    Task<IReadOnlyList<EmployeeOptionDto>> GetManagerOptionsAsync(CancellationToken ct = default);

    /// <summary>Active departments, plus <paramref name="includeId"/> even if inactive (for editing an existing record).</summary>
    Task<IReadOnlyList<DepartmentOptionDto>> GetOptionsAsync(int? includeId = null, CancellationToken ct = default);

    Task<Result<int>> CreateAsync(DepartmentEditDto dto, CancellationToken ct = default);
    Task<Result> UpdateAsync(DepartmentEditDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
}
