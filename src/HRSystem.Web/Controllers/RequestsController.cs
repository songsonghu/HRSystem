using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Domain.Enums;
using HRSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Web.Controllers;

/// <summary>
/// Account provisioning request UI (Modules 3 &amp; 4). HR creates/submits
/// requests; Admin can view everyone's status.
/// </summary>
[Authorize(Policy = "RequireHR")]
public class RequestsController : Controller
{
    private readonly IAccountRequestService _service;
    private readonly IEmployeeService _employeeService;
    private readonly AppDbContext _db;

    public RequestsController(IAccountRequestService service, IEmployeeService employeeService, AppDbContext db)
    {
        _service = service;
        _employeeService = employeeService;
        _db = db;
    }

    // GET: /Requests  -> admin dashboard of all requests & their status
    public async Task<IActionResult> Index()
    {
        var all = await _service.GetAllAsync();
        return View(all);
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
        await PopulateSelectListsAsync(employeeId);
        return View(new CreateRequestDto { EmployeeId = employeeId ?? 0 });
    }

    // POST: /Requests/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateRequestDto dto)
    {
        if (!ModelState.IsValid)
        {
            await PopulateSelectListsAsync(dto.EmployeeId);
            return View(dto);
        }

        var result = await _service.CreateAsync(dto);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            await PopulateSelectListsAsync(dto.EmployeeId);
            return View(dto);
        }

        TempData["Success"] = "Draft request created. Review and submit to dispatch.";
        return RedirectToAction(nameof(Details), new { id = result.Value });
    }

    // POST: /Requests/Submit/5 -> dispatch + trigger emails
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id)
    {
        var result = await _service.SubmitAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded ? "Request submitted. Notifications sent to departments." : result.Error;
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
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded ? "File uploaded." : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Populate the employee picker and the account-type checklist for the
    /// create form. The checklist is filtered to the Staff or AE/Sales/SA
    /// form (matching the paper requisition forms) based on the selected
    /// employee's category.
    /// </summary>
    private async Task PopulateSelectListsAsync(int? employeeId)
    {
        var employees = await _db.Employees.AsNoTracking()
            .OrderBy(e => e.Name)
            .Select(e => new SelectListItem($"{e.Name} ({e.EmployeeNo})", e.Id.ToString()))
            .ToListAsync();

        var selectedEmployee = employeeId is > 0
            ? await _employeeService.GetAsync(employeeId.Value)
            : null;

        var accountTypeGroups = selectedEmployee is null
            ? new List<IGrouping<string, AccountTypeOptionDto>>()
            : (await _service.GetAccountTypeOptionsAsync(selectedEmployee.Id))
                .GroupBy(a => a.DeptName)
                .ToList();

        ViewBag.Employees = employees;
        ViewBag.SelectedEmployee = selectedEmployee;
        ViewBag.AccountTypeGroups = accountTypeGroups;
        ViewBag.RequestTypes = Enum.GetValues<RequestType>()
            .Select(t => new SelectListItem(t.ToString(), ((int)t).ToString()))
            .ToList();
    }
}
