using HRSystem.Application.Common;
using HRSystem.Application.DTOs;

namespace HRSystem.Application.Interfaces;

/// <summary>Employee management use cases (Module 2).</summary>
public interface IEmployeeService
{
    Task<IReadOnlyList<EmployeeDto>> GetListAsync(string? keyword = null, CancellationToken ct = default);
    Task<EmployeeDto?> GetAsync(int id, CancellationToken ct = default);
    Task<Result<int>> CreateAsync(EmployeeEditDto dto, CancellationToken ct = default);
    Task<Result> UpdateAsync(EmployeeEditDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
}
