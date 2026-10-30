using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HRSystem.Web.ViewModels;

/// <summary>
/// The "who handles and approves" block of a template page. The pickers post as
/// <c>HandlerUserIds</c> and <c>ApproverUserIds</c>.
/// </summary>
public class AssigneesEditorViewModel
{
    public DepartmentAssignmentDto Assignees { get; set; } = new();

    /// <summary>Only holders of checklist-approvers.manage may change the people; others see their names.</summary>
    public bool CanEdit { get; set; }

    public List<SelectListItem> Users { get; set; } = new();

    /// <summary>Selected logins resolved to display names, for the read-only view.</summary>
    public IReadOnlyDictionary<string, string> Names { get; set; } = new Dictionary<string, string>();

    public UserPickerViewModel Picker(string name, IReadOnlyList<string> selected)
        => new() { Name = name, Selected = selected, Users = Users, Fallback = Assignees.ManagerUserName };

    /// <summary>Active logins for the pickers, plus any selected login that is disabled so saving keeps it.</summary>
    public static async Task<AssigneesEditorViewModel> CreateAsync(
        DepartmentAssignmentDto assignees, bool canEdit, IUserDirectoryService users)
    {
        var selected = assignees.HandlerUserIds.Concat(assignees.ApproverUserIds).Distinct().ToList();
        var known = await users.GetByIdsAsync(selected);
        var options = canEdit
            ? (await users.GetActiveUsersAsync()).Select(u => new SelectListItem(u.Display, u.UserId)).ToList()
            : new List<SelectListItem>();
        if (canEdit)
        {
            foreach (var user in known.Values.Where(u => options.All(o => o.Value != u.UserId)))
                options.Add(new SelectListItem($"{user.Display} (disabled)", user.UserId));
        }

        return new AssigneesEditorViewModel
        {
            Assignees = assignees,
            CanEdit = canEdit,
            Users = options,
            Names = known.ToDictionary(kv => kv.Key, kv => kv.Value.Display)
        };
    }
}

public class UserPickerViewModel
{
    public string Name { get; set; } = string.Empty;
    public IReadOnlyList<string> Selected { get; set; } = Array.Empty<string>();
    public List<SelectListItem> Users { get; set; } = new();

    /// <summary>Shown when nobody is ticked: the manager's login, which then takes the role.</summary>
    public string? Fallback { get; set; }
}
