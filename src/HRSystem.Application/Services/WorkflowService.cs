using HRSystem.Domain.Entities;
using HRSystem.Domain.Enums;

namespace HRSystem.Application.Services;

/// <summary>
/// Central state-machine authority for the account provisioning workflow.
/// All status transitions must pass through this service so illegal jumps are
/// rejected in one place.
/// </summary>
public class WorkflowService
{
    // Allowed transitions for a single request item.
    private static readonly Dictionary<ItemStatus, ItemStatus[]> ItemTransitions = new()
    {
        [ItemStatus.NotStarted] = new[] { ItemStatus.WIP, ItemStatus.KIV, ItemStatus.Completed, ItemStatus.Rejected },
        [ItemStatus.WIP]        = new[] { ItemStatus.Completed, ItemStatus.KIV, ItemStatus.Rejected },
        [ItemStatus.KIV]        = new[] { ItemStatus.WIP, ItemStatus.Completed, ItemStatus.Rejected },
        [ItemStatus.Completed]  = Array.Empty<ItemStatus>(), // terminal
        [ItemStatus.Rejected]   = new[] { ItemStatus.WIP }   // may re-open on appeal
    };

    /// <summary>Returns true when the item may move from current to target.</summary>
    public bool CanTransition(ItemStatus current, ItemStatus target)
        => current == target || ItemTransitions[current].Contains(target);

    /// <summary>Throws if the requested item transition is not allowed.</summary>
    public void EnsureItemTransition(ItemStatus current, ItemStatus target)
    {
        if (!CanTransition(current, target))
            throw new InvalidOperationException($"Illegal item transition: {current} -> {target}.");
    }

    /// <summary>
    /// Recompute the master request status from the aggregate state of its items.
    /// - All Completed/Rejected  => Completed
    /// - Any WIP/Completed/KIV   => InProgress
    /// - Otherwise               => Submitted
    /// </summary>
    public RequestStatus EvaluateRequestStatus(IEnumerable<AccountRequestItem> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return RequestStatus.Submitted;

        bool allDone = list.All(i => i.Status is ItemStatus.Completed or ItemStatus.Rejected);
        if (allDone) return RequestStatus.Completed;

        bool anyProgress = list.Any(i => i.Status is ItemStatus.WIP or ItemStatus.Completed or ItemStatus.KIV);
        return anyProgress ? RequestStatus.InProgress : RequestStatus.Submitted;
    }
}
