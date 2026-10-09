using HRSystem.Application.Interfaces;
using HRSystem.Domain.Enums;
using HRSystem.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HRSystem.Web.Controllers;

/// <summary>Shared actions for onboarding and departure requests; views live in Views/Checklists.</summary>
public abstract class ChecklistRequestsControllerBase : Controller
{
    private readonly IChecklistService _service;

    protected ChecklistRequestsControllerBase(IChecklistService service) => _service = service;

    protected abstract ChecklistKind Kind { get; }

    public async Task<IActionResult> Index()
    {
        ViewBag.Kind = Kind;
        ViewBag.Employees = (await _service.GetEligibleEmployeesAsync(Kind))
            .Select(e => new SelectListItem(e.Display, e.Id.ToString()))
            .ToList();
        return View("~/Views/Checklists/Index.cshtml", await _service.GetListAsync(Kind));
    }

    public async Task<IActionResult> Create(int employeeId)
    {
        var openRequestId = await _service.GetOpenRequestIdAsync(Kind, employeeId);
        if (openRequestId.HasValue)
        {
            TempData["Success"] = "This employee already has an open request. Redirected to it.";
            return RedirectToAction(nameof(Details), new { id = openRequestId.Value });
        }

        var page = await _service.GetCreatePageAsync(Kind, employeeId);
        if (!page.Succeeded)
        {
            TempData["Error"] = page.Error;
            return RedirectToAction(nameof(Index));
        }

        return View("~/Views/Checklists/Create.cshtml", ChecklistViewModelMapper.FromCreatePage(page.Value!));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ChecklistCreateViewModel vm)
    {
        vm.Kind = Kind;
        if (ModelState.IsValid)
        {
            var result = await _service.CreateDraftAsync(vm.ToCreateDto());
            if (result.Succeeded)
            {
                TempData["Success"] = "Draft created. Review it, then submit.";
                return RedirectToAction(nameof(Details), new { id = result.Value });
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }

        var page = await _service.GetCreatePageAsync(Kind, vm.EmployeeId);
        if (!page.Succeeded)
        {
            TempData["Error"] = page.Error;
            return RedirectToAction(nameof(Index));
        }
        vm.CopyDisplayFrom(page.Value!);
        return View("~/Views/Checklists/Create.cshtml", vm);
    }

    public async Task<IActionResult> Details(int id)
    {
        var request = await _service.GetAsync(id);
        if (request is null || request.Kind != Kind) return NotFound();
        return View("~/Views/Checklists/Details.cshtml", request);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id)
    {
        var result = await _service.SubmitAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Submitted. Each department manager has been notified."
            : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Finalize(int id)
    {
        var result = await _service.FinalizeAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? (Kind == ChecklistKind.Departure ? "Departure finalized and employee marked as resigned." : "Onboarding finalized.")
            : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }
}
