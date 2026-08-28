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
    private readonly AppDbContext _db;

    public RequestsController(IAccountRequestService service, AppDbContext db)
    {
        _service = service;
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
        await PopulateSelectListsAsync();
        return View(new CreateRequestDto { EmployeeId = employeeId ?? 0 });
    }

    // POST: /Requests/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateRequestDto dto)
    {
        if (!ModelState.IsValid)
        {
            await PopulateSelectListsAsync();
            return View(dto);
        }

        var result = await _service.CreateAsync(dto);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            await PopulateSelectListsAsync();
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

    /// <summary>Populate employee and account-type pickers for the create form.</summary>
    private async Task PopulateSelectListsAsync()
    {
        var employees = await _db.Employees.AsNoTracking()
            .OrderBy(e => e.Name)
            .Select(e => new SelectListItem($"{e.Name} ({e.EmployeeNo})", e.Id.ToString()))
            .ToListAsync();

        var accountTypes = await _db.AccountTypes.AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.SortOrder)
            .Select(a => new { a.Id, a.Name })
            .ToListAsync();

        ViewBag.Employees = employees;
        ViewBag.AccountTypes = accountTypes;
        ViewBag.RequestTypes = Enum.GetValues<RequestType>()
            .Select(t => new SelectListItem(t.ToString(), ((int)t).ToString()))
            .ToList();
    }
}
