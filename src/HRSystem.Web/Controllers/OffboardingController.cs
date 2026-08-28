using HRSystem.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRSystem.Web.Controllers;

/// <summary>
/// Offboarding account listing and Excel export (Module 5). Restricted to Admin/HR.
/// </summary>
[Authorize(Policy = "RequireHR")]
public class OffboardingController : Controller
{
    private readonly IOffboardingService _service;
    private readonly IEmployeeService _employees;

    public OffboardingController(IOffboardingService service, IEmployeeService employees)
    {
        _service = service;
        _employees = employees;
    }

    // GET: /Offboarding/Employee/5 -> list of all active accounts for the employee
    public async Task<IActionResult> Employee(int id)
    {
        var employee = await _employees.GetAsync(id);
        if (employee is null) return NotFound();

        var rows = await _service.GetActiveAccountsAsync(id);
        ViewBag.Employee = employee;
        return View(rows);
    }

    // GET: /Offboarding/Export/5 -> download the account checklist as .xlsx
    public async Task<IActionResult> Export(int id)
    {
        var employee = await _employees.GetAsync(id);
        if (employee is null) return NotFound();

        var bytes = await _service.ExportExcelAsync(id);
        var fileName = $"Offboarding_{employee.EmployeeNo}_{DateTime.UtcNow:yyyyMMdd}.xlsx";
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }
}
