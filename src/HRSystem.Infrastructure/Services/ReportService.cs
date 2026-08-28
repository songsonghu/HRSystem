using ClosedXML.Excel;
using HRSystem.Application.Interfaces;

namespace HRSystem.Infrastructure.Services;

/// <summary>
/// Builds Excel reports using ClosedXML. Currently produces the offboarding
/// account de-provisioning checklist (Module 5).
/// </summary>
public class ReportService : IReportService
{
    public byte[] BuildOffboardingWorkbook(
        string employeeNo, string employeeName, IEnumerable<OffboardAccountRow> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Offboarding");

        // Title block
        ws.Cell(1, 1).Value = "Employee Account De-provisioning Checklist";
        ws.Range(1, 1, 1, 4).Merge().Style.Font.SetBold().Font.FontSize = 14;
        ws.Cell(2, 1).Value = "Employee No.:";
        ws.Cell(2, 2).Value = employeeNo;
        ws.Cell(3, 1).Value = "Employee Name:";
        ws.Cell(3, 2).Value = employeeName;
        ws.Cell(4, 1).Value = "Generated (UTC):";
        ws.Cell(4, 2).Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");

        // Header row
        const int headerRow = 6;
        ws.Cell(headerRow, 1).Value = "#";
        ws.Cell(headerRow, 2).Value = "Account Type";
        ws.Cell(headerRow, 3).Value = "Account Value";
        ws.Cell(headerRow, 4).Value = "Status";
        ws.Cell(headerRow, 5).Value = "Opened At (UTC)";
        var header = ws.Range(headerRow, 1, headerRow, 5);
        header.Style.Font.SetBold();
        header.Style.Fill.BackgroundColor = XLColor.LightGray;
        header.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

        // Data rows
        int r = headerRow + 1;
        int idx = 1;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = idx++;
            ws.Cell(r, 2).Value = row.AccountTypeName;
            ws.Cell(r, 3).Value = row.AccountValue ?? string.Empty;
            ws.Cell(r, 4).Value = row.Status;
            ws.Cell(r, 5).Value = row.OpenedAt.ToString("yyyy-MM-dd");
            r++;
        }

        if (idx == 1)
            ws.Cell(r, 2).Value = "(No active accounts found)";

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
