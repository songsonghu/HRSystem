using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HRSystem.Web.Controllers;

[Authorize(Policy = Permissions.DepartmentsManage)]
public class DepartmentsController : Controller
{
    private readonly IDepartmentService _service;

    public DepartmentsController(IDepartmentService service) => _service = service;

    public async Task<IActionResult> Index() => View(await _service.GetListAsync());

    public async Task<IActionResult> Details(int id)
    {
        var dto = await _service.GetAsync(id);
        return dto is null ? NotFound() : View(dto);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateManagersAsync();
        return View(new DepartmentEditDto());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DepartmentEditDto dto)
    {
        if (ModelState.IsValid)
        {
            var result = await _service.CreateAsync(dto);
            if (result.Succeeded)
            {
                TempData["Success"] = $"Department {dto.Name} created.";
                return RedirectToAction(nameof(Details), new { id = result.Value });
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }

        await PopulateManagersAsync();
        return View(dto);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var details = await _service.GetAsync(id);
        if (details is null) return NotFound();

        await PopulateManagersAsync();
        return View(new DepartmentEditDto
        {
            Id = details.Id,
            Name = details.Name,
            Code = details.Code,
            ManagerEmployeeId = details.ManagerEmployeeId,
            IsActive = details.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(DepartmentEditDto dto)
    {
        if (ModelState.IsValid)
        {
            var result = await _service.UpdateAsync(dto);
            if (result.Succeeded)
            {
                TempData["Success"] = $"Department {dto.Name} updated.";
                return RedirectToAction(nameof(Details), new { id = dto.Id });
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }

        await PopulateManagersAsync();
        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _service.DeleteAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Department deleted." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateManagersAsync()
    {
        ViewBag.Managers = (await _service.GetManagerOptionsAsync())
            .Select(e => new SelectListItem(e.Display, e.Id.ToString()))
            .ToList();
    }
}
