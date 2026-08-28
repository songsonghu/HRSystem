using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRSystem.Web.Controllers;

/// <summary>
/// Department-head workbench (Module 3, department side). Shows only the items
/// dispatched to the current user's department and lets them provision/close.
/// </summary>
[Authorize(Policy = "RequireDeptHead")]
public class MyTasksController : Controller
{
    private readonly IAccountRequestService _service;

    public MyTasksController(IAccountRequestService service) => _service = service;

    // GET: /MyTasks -> pending items for the current department head
    public async Task<IActionResult> Index()
    {
        var items = await _service.GetMyPendingItemsAsync();
        return View(items);
    }

    // POST: /MyTasks/Update -> set status / account value / remark on an item
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(UpdateItemDto dto)
    {
        var result = await _service.UpdateItemAsync(dto);
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded ? "Item updated." : result.Error;
        return RedirectToAction(nameof(Index));
    }
}
