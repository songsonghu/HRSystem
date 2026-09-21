using HRSystem.Application.Interfaces;
using HRSystem.Domain.Enums;
using HRSystem.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRSystem.Web.Controllers;

[Authorize]
public class DeparturesController : Controller
{
    private readonly IDepartureService _service;

    public DeparturesController(IDepartureService service) => _service = service;

    [Authorize(Policy = "RequireHR")]
    public async Task<IActionResult> Create(int employeeId)
    {
        var page = await _service.GetCreatePageAsync(employeeId);
        if (!page.Succeeded)
        {
            TempData["Error"] = page.Error;
            return RedirectToAction("Index", "Employees");
        }

        return View(DepartureViewModelMapper.FromCreatePage(page.Value!));
    }

    [HttpPost]
    [Authorize(Policy = "RequireHR")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DepartureCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            var page = await _service.GetCreatePageAsync(vm.EmployeeId);
            if (page.Succeeded)
                vm.TemplateDepartments = page.Value!.TemplateDepartments;
            return View(vm);
        }

        var result = await _service.CreateDraftAsync(vm.ToCreateDto());
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            var page = await _service.GetCreatePageAsync(vm.EmployeeId);
            if (page.Succeeded)
                vm.TemplateDepartments = page.Value!.TemplateDepartments;
            return View(vm);
        }

        TempData["Success"] = "Departure draft created.";
        return RedirectToAction(nameof(Details), new { id = result.Value });
    }

    [Authorize(Policy = "RequireHR")]
    public async Task<IActionResult> Details(int id)
    {
        var request = await _service.GetAsync(id);
        if (request is null) return NotFound();

        var vm = new DepartureDetailsViewModel
        {
            Request = request,
            CanManage = true,
            CanSubmit = request.Status == DepartureRequestStatus.Draft,
            CanFinalize = request.Status == DepartureRequestStatus.PendingFinalReview
        };
        return View(vm);
    }

    [HttpPost]
    [Authorize(Policy = "RequireHR")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id)
    {
        var result = await _service.SubmitAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Departure request submitted and department heads notified."
            : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = "RequireHR")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Finalize(int id)
    {
        var result = await _service.FinalizeAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Departure request finalized and employee marked as resigned."
            : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = "RequireDeptHead")]
    public async Task<IActionResult> MyTasks()
    {
        var tasks = await _service.GetMyPendingTasksAsync();
        return View(tasks);
    }

    [Authorize(Policy = "RequireDeptHead")]
    public async Task<IActionResult> Task(int id)
    {
        var task = await _service.GetTaskAsync(id);
        if (task is null) return NotFound();
        return View(task.ToTaskEdit());
    }

    [HttpPost]
    [Authorize(Policy = "RequireDeptHead")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Task(DepartureTaskEditViewModel vm)
    {
        var result = await _service.UpdateTaskAsync(vm.ToUpdateDto());
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            var task = await _service.GetTaskAsync(vm.TaskId);
            if (task is null) return NotFound();
            return View(task.ToTaskEdit());
        }

        TempData["Success"] = vm.MarkAsCompleted ? "Department task submitted." : "Department task saved.";
        return RedirectToAction(nameof(MyTasks));
    }
}
