using HRSystem.Application.DTOs;

namespace HRSystem.Application.Interfaces;

/// <summary>A single row in the offboarding account listing / export.</summary>
public record OffboardAccountRow(
    string AccountTypeName,
    string? AccountValue,
    string Status,
    DateTime OpenedAt);

/// <summary>Offboarding listing and report export use cases (Module 5).</summary>
public interface IOffboardingService
{
    /// <summary>List all currently active accounts held by an employee.</summary>
    Task<IReadOnlyList<OffboardAccountRow>> GetActiveAccountsAsync(int employeeId, CancellationToken ct = default);

    /// <summary>Export the offboarding account listing as an Excel workbook.</summary>
    Task<byte[]> ExportExcelAsync(int employeeId, CancellationToken ct = default);
}
