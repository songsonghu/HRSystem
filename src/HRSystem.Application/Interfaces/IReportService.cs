using HRSystem.Application.Interfaces;

namespace HRSystem.Application.Interfaces;

/// <summary>Generates report files (Excel) from tabular data.</summary>
public interface IReportService
{
    /// <summary>
    /// Build an Excel workbook for the offboarding account listing of an employee.
    /// </summary>
    byte[] BuildOffboardingWorkbook(
        string employeeNo,
        string employeeName,
        IEnumerable<OffboardAccountRow> rows);
}
