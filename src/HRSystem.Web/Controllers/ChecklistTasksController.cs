using HRSystem.Application.DTOs;
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

    public async Task<IActionResult> History(ChecklistTaskHistoryQuery query)
    {
        ViewBag.Query = query;
        return View(await _service.GetMyTaskHistoryAsync(query));
    }

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
            if (task is null) return NotFound();
            // Keep what the user entered; only the read-only parts come from the database.
            var redisplay = task.ToTaskEdit();
            redisplay.TaskRemark = vm.TaskRemark;
            foreach (var item in redisplay.Items)
            {
                var posted = vm.Items.FirstOrDefault(i => i.Id == item.Id);
                if (posted is null) continue;
                item.IsCompleted = posted.IsCompleted;
                item.IsNotApplicable = posted.IsNotApplicable;
                item.Remark = posted.Remark;
            }
            return View(redisplay);
        }

        TempData["Success"] = vm.MarkAsCompleted
            ? "Department task completed. It now appears under History."
            : "Progress saved. Click \"Complete Task\" when every required item is done.";
        return vm.MarkAsCompleted ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Task), new { id = vm.TaskId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(int id)
    {
        var result = await _service.ReopenTaskAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Task taken back. Change it and complete it again."
            : result.Error;
        return result.Succeeded ? RedirectToAction(nameof(Task), new { id }) : RedirectToAction(nameof(History));
    }
}
