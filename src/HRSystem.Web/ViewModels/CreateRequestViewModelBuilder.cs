using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HRSystem.Application.DTOs;
using HRSystem.Domain.Enums;
using HRSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Web.ViewModels
{
    /// <summary>
    /// Populates the render-only parts of <see cref="CreateRequestViewModel"/>
    /// (employee/request-type dropdowns and the grouped, layout-aware item list)
    /// and maps the submitted view model to <see cref="CreateRequestDto"/>.
    ///
    /// Keeps the controller thin and re-applies the user's ticked items and
    /// typed details so they survive a redisplay after validation errors.
    ///
    /// Registered in DI (scoped) and injected into RequestsController.
    /// </summary>
    public class CreateRequestViewModelBuilder
    {
        private readonly AppDbContext _db;

        // Fixed section order matching the paper form.
        private static readonly string[] GroupOrder =
        {
            "Client Service & Internet Trading",
            "Credit Department",
            "IT Department",
            "HR & Administration Department"
        };

        public CreateRequestViewModelBuilder(AppDbContext db) => _db = db;

        /// <summary>
        /// Fill EmployeeOptions / RequestTypeOptions / Groups onto the VM,
        /// preserving current selections (AccountTypeIds) and typed details.
        /// </summary>
        public async Task PopulateOptionsAsync(CreateRequestViewModel vm, CancellationToken ct = default)
        {
            vm.EmployeeOptions = await BuildEmployeeOptionsAsync(vm.EmployeeId, ct);
            vm.RequestTypeOptions = BuildRequestTypeOptions(vm.RequestType);
            vm.Groups = await BuildGroupsAsync(vm.AccountTypeIds, vm.Details, ct);
        }

        private async Task<IReadOnlyList<SelectListItem>> BuildEmployeeOptionsAsync(int selectedId, CancellationToken ct)
            => await _db.Employees.AsNoTracking()
                .OrderBy(e => e.Name)
                .Select(e => new SelectListItem
                {
                    Value = e.Id.ToString(),
                    Text = $"{e.Name} ({e.EmployeeNo})",
                    Selected = e.Id == selectedId
                })
                .ToListAsync(ct);

        private static IReadOnlyList<SelectListItem> BuildRequestTypeOptions(RequestType selected)
            => Enum.GetValues<RequestType>()
                .Select(t => new SelectListItem
                {
                    Value = ((int)t).ToString(),
                    Text = t.ToString(),
                    Selected = t == selected
                })
                .ToList();

        private async Task<IReadOnlyList<AccountTypeGroup>> BuildGroupsAsync(
            ICollection<int> selectedIds, IDictionary<int, string?> details, CancellationToken ct)
        {
            var selected = selectedIds?.ToHashSet() ?? new HashSet<int>();
            details ??= new Dictionary<int, string?>();

            // Load active items with their layout metadata.
            var rows = await _db.AccountTypes.AsNoTracking()
                .Where(a => a.IsActive)
                .Select(a => new AccountTypeOption
                {
                    Id = a.Id,
                    Name = a.Name,
                    Column = a.Column,
                    Row = a.SortOrder,
                    HasCheckbox = a.HasCheckbox,
                    RequiresDetail = a.RequiresDetail,
                    DetailLabel = a.DetailLabel,
                    PrefixText = a.PrefixText,
                    SuffixText = a.SuffixText,
                    // group key is carried separately below
                })
                .ToListAsync(ct);

            // We need GroupName too; fetch it alongside (kept separate to keep the
            // projection above focused). A second lightweight projection avoids
            // changing the option type.
            var groupNames = await _db.AccountTypes.AsNoTracking()
                .Where(a => a.IsActive)
                .Select(a => new { a.Id, a.GroupName })
                .ToDictionaryAsync(x => x.Id, x => x.GroupName ?? "Others", ct);

            // Re-apply user state.
            foreach (var opt in rows)
            {
                opt.IsSelected = selected.Contains(opt.Id);
                opt.DetailValue = details.TryGetValue(opt.Id, out var v) ? v : null;
            }

            // Group + order to match the paper form.
            return rows
                .GroupBy(o => groupNames.TryGetValue(o.Id, out var g) ? g : "Others")
                .OrderBy(g => IndexOfGroup(g.Key))
                .Select(g => new AccountTypeGroup
                {
                    GroupName = g.Key,
                    Items = g.OrderBy(i => i.Row).ThenBy(i => i.Column).ToList()
                })
                .ToList();
        }

        private static int IndexOfGroup(string name)
        {
            var idx = Array.IndexOf(GroupOrder, name);
            return idx < 0 ? int.MaxValue : idx;   // unknown groups go last
        }

        /// <summary>ViewModel → DTO boundary. The service only ever sees the DTO.</summary>
        public static CreateRequestDto ToDto(CreateRequestViewModel vm) => new()
        {
            EmployeeId = vm.EmployeeId,
            RequestType = vm.RequestType,
            AccountTypeIds = vm.AccountTypeIds ?? new List<int>(),
            // keep only details that belong to a ticked item
            Details = (vm.Details ?? new Dictionary<int, string?>())
                .Where(kv => vm.AccountTypeIds != null && vm.AccountTypeIds.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value),
            Remark = vm.Remark
        };
    }
}
