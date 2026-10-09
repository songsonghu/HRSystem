using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRSystem.Web.Controllers;

[Authorize(Policy = Permissions.RolesManage)]
public class RolesController : Controller
{
    private readonly IRoleAdminService _service;

    public RolesController(IRoleAdminService service) => _service = service;

    public async Task<IActionResult> Index() => View(await _service.GetListAsync());

    public IActionResult Create() => View(new RoleEditDto());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RoleEditDto dto)
    {
        if (!ModelState.IsValid) return View(dto);

        var result = await _service.CreateAsync(dto);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(dto);
        }

        TempData["Success"] = $"Role {dto.Name} created.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id)
    {
        var dto = await _service.GetAsync(id);
        return dto is null ? NotFound() : View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(RoleEditDto dto)
    {
        if (!ModelState.IsValid) return View(dto);

        var result = await _service.UpdateAsync(dto);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(dto);
        }

        TempData["Success"] = $"Role {dto.Name} updated. Signed-in users pick up the change within about a minute.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var result = await _service.DeleteAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Role deleted." : result.Error;
        return RedirectToAction(nameof(Index));
    }
}
