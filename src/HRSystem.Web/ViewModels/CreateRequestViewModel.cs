using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using HRSystem.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HRSystem.Web.ViewModels
{
    /// <summary>
    /// View model for the "Create Account Request" page.
    ///
    /// Carries three things a DTO must NOT carry:
    ///   1) INPUT     — form fields (with validation attributes).
    ///   2) UI STATE  — dropdown / checkbox option sources (was ViewBag).
    ///   3) LAYOUT    — the paper-form layout metadata per account type,
    ///                  so the view can be reproduced exactly and data-driven.
    ///
    /// On validation failure the controller re-populates the option sources and
    /// re-applies the user's selections/details, so nothing the user typed is lost.
    /// </summary>
    public class CreateRequestViewModel
    {
        // -------------------- 1) user input (posted back) --------------------

        [Required(ErrorMessage = "Please select an employee.")]
        [Display(Name = "Employee")]
        public int EmployeeId { get; set; }

        [Required(ErrorMessage = "Please choose a request type.")]
        [Display(Name = "Request Type")]
        public RequestType RequestType { get; set; } = RequestType.Onboard;

        /// <summary>Account type ids the user ticked. Bound from name="AccountTypeIds".</summary>
        [Display(Name = "Items Requested")]
        [MinLength(1, ErrorMessage = "Select at least one item.")]
        public List<int> AccountTypeIds { get; set; } = new();

        /// <summary>
        /// Per-item free-text details. Bound from name="Details[{id}]".
        /// Key = AccountType.Id, Value = the text the user typed on the fill-in line.
        /// </summary>
        public Dictionary<int, string?> Details { get; set; } = new();

        [Display(Name = "Remark")]
        [StringLength(1000, ErrorMessage = "Remark cannot exceed 1000 characters.")]
        [DataType(DataType.MultilineText)]
        public string? Remark { get; set; }

        // -------------------- 2) render-only UI state ------------------------

        /// <summary>Chooses the title wording of the requested-items box.</summary>
        public bool IsAe { get; set; } = true;

        public IReadOnlyList<SelectListItem> EmployeeOptions { get; set; } = new List<SelectListItem>();
        public IReadOnlyList<SelectListItem> RequestTypeOptions { get; set; } = new List<SelectListItem>();

        /// <summary>
        /// All selectable items with their paper-form layout metadata, already
        /// grouped and ordered as the form sections should appear.
        /// </summary>
        public IReadOnlyList<AccountTypeGroup> Groups { get; set; } = new List<AccountTypeGroup>();
    }

    /// <summary>A form section (e.g. "IT Department") with its items.</summary>
    public class AccountTypeGroup
    {
        public string GroupName { get; set; } = string.Empty;
        public List<AccountTypeOption> Items { get; set; } = new();
    }

    /// <summary>
    /// One selectable item plus everything needed to reproduce the paper form
    /// and to preserve the user's input on redisplay.
    /// </summary>
    public class AccountTypeOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        // ---- layout metadata (mirrors AccountType fields) ----
        public int Column { get; set; } = 1;        // 1 = left, 2 = right
        public int Row { get; set; }                // = AccountType.SortOrder
        public bool HasCheckbox { get; set; } = true;
        public bool RequiresDetail { get; set; }
        public string? DetailLabel { get; set; }
        public string? PrefixText { get; set; }
        public string? SuffixText { get; set; }

        // ---- redisplay state ----
        public bool IsSelected { get; set; }
        public string? DetailValue { get; set; }

        /// <summary>True when this item is an "Access Right - X" inline chip.</summary>
        public bool IsAccessRight => Name.StartsWith("Access Right - ");

        /// <summary>Short label for an access-right chip (text after the prefix).</summary>
        public string ShortName => IsAccessRight ? Name["Access Right - ".Length..] : Name;
    }
}
