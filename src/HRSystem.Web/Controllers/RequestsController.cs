using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HRSystem.Web.Controllers;

/// <summary>
/// Account requests. HR sees and raises requests for everyone; managers for their
/// department; employees for themselves. Access rules are enforced by the service.
/// </summary>
[Authorize]
public class RequestsController : Controller
{
    private readonly IAccountRequestService _service;
    private readonly IEmployeeService _employeeService;

    public RequestsController(IAccountRequestService service, IEmployeeService employeeService)
    {
        _service = service;
        _employeeService = employeeService;
    }

    // GET: /Requests
    public async Task<IActionResult> Index()
    {
        ViewBag.PendingApprovals = await _service.GetPendingApprovalsAsync();
        ViewBag.CanCreate = (await _service.GetCreateContextAsync()).Employees.Count > 0;
        return View(await _service.GetListAsync());
    }

    // GET: /Requests/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var dto = await _service.GetAsync(id);
        return dto is null ? NotFound() : View(dto);
    }

    // GET: /Requests/Create
    public async Task<IActionResult> Create(int? employeeId)
    {
        var context = await _service.GetCreateContextAsync();
        if (context.Employees.Count == 0)
        {
            TempData["Error"] = "Your login is not linked to an employee record, so you cannot raise requests. Please contact HR.";
            return RedirectToAction(nameof(Index));
        }

        // Pre-select the only choice (typically an employee requesting for themself).
        employeeId ??= context.Employees.Count == 1 ? context.Employees[0].Id : null;
        await PopulateFormAsync(context, employeeId);
        return View(new CreateRequestDto
        {
            EmployeeId = employeeId ?? 0,
            RequestType = context.RequestTypes[0]
        });
    }

    // POST: /Requests/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateRequestDto dto)
    {
        if (ModelState.IsValid)
        {
            var result = await _service.CreateAsync(dto);
            if (result.Succeeded)
            {
                TempData["Success"] = "Draft request created. Review it, then submit.";
                return RedirectToAction(nameof(Details), new { id = result.Value });
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }

        await PopulateFormAsync(await _service.GetCreateContextAsync(), dto.EmployeeId);
        return View(dto);
    }

    // POST: /Requests/Submit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id)
    {
        var result = await _service.SubmitAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Request submitted." : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: /Requests/Approve/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var result = await _service.ApproveAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded ? "Request approved and sent to the responsible departments." : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: /Requests/Reject/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? reason)
    {
        var result = await _service.RejectAsync(id, reason ?? string.Empty);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Request rejected." : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: /Requests/Upload/5 -> attach a scanned signed approval document
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Upload(int id, IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Please choose a file.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await using var stream = file.OpenReadStream();
        var result = await _service.AddAttachmentAsync(id, stream, file.FileName, file.ContentType);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "File uploaded." : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Employee picker (restricted to who the user may raise requests for) and the
    /// account-type checklist for the selected employee's form (Staff or AE/Sales/SA).
    /// </summary>
    private async Task PopulateFormAsync(RequestCreateContextDto context, int? employeeId)
    {
        var allowed = employeeId is > 0 && context.Employees.Any(e => e.Id == employeeId);
        var selectedEmployee = allowed ? await _employeeService.GetAsync(employeeId!.Value) : null;

        ViewBag.Employees = context.Employees
            .Select(e => new SelectListItem(e.Display, e.Id.ToString()))
            .ToList();
        ViewBag.SelectedEmployee = selectedEmployee;
        ViewBag.AccountTypeGroups = selectedEmployee is null
            ? new List<IGrouping<string, AccountTypeOptionDto>>()
            : (await _service.GetAccountTypeOptionsAsync(selectedEmployee.Id)).GroupBy(a => a.DeptName).ToList();
        ViewBag.RequestTypes = context.RequestTypes
            .Select(t => new SelectListItem(t.ToString(), ((int)t).ToString()))
            .ToList();
    }
}
