using HRSystem.Application.Interfaces;
using HRSystem.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRSystem.Web.Controllers;

/// <summary>Onboarding and departure checklist tasks assigned to the current user.</summary>
[Authorize]
public class ChecklistTasksController : Controller
{
    private readonly IChecklistService _service;

    public ChecklistTasksController(IChecklistService service) => _service = service;

    public async Task<IActionResult> Index() => View(await _service.GetMyPendingTasksAsync());

    public async Task<IActionResult> Task(int id)
    {
        var task = await _service.GetTaskAsync(id);
        return task is null ? NotFound() : View(task.ToTaskEdit());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Task(ChecklistTaskEditViewModel vm)
    {
        var result = await _service.UpdateTaskAsync(vm.ToUpdateDto());
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            var task = await _service.GetTaskAsync(vm.TaskId);
            return task is null ? NotFound() : View(task.ToTaskEdit());
        }

        TempData["Success"] = vm.MarkAsCompleted ? "Department task submitted." : "Department task saved.";
        return RedirectToAction(nameof(Index));
    }
}
