using HRSystem.Application.Services;
using HRSystem.Domain.Entities;
using HRSystem.Domain.Enums;

namespace HRSystem.Tests.Unit;

public class WorkflowServiceTests
{
    private readonly WorkflowService _workflow = new();

    private static AccountRequestItem Item(ItemStatus status) => new() { Status = status };

    [Fact]
    public void Request_without_items_counts_as_submitted()
        => Assert.Equal(RequestStatus.Submitted, _workflow.EvaluateRequestStatus(Array.Empty<AccountRequestItem>()));

    [Fact]
    public void Untouched_items_keep_request_submitted()
        => Assert.Equal(RequestStatus.Submitted,
            _workflow.EvaluateRequestStatus(new[] { Item(ItemStatus.NotStarted), Item(ItemStatus.NotStarted) }));

    [Theory]
    [InlineData(ItemStatus.WIP)]
    [InlineData(ItemStatus.KIV)]
    [InlineData(ItemStatus.Completed)]
    public void Any_progress_makes_request_in_progress(ItemStatus progressed)
        => Assert.Equal(RequestStatus.InProgress,
            _workflow.EvaluateRequestStatus(new[] { Item(progressed), Item(ItemStatus.NotStarted) }));

    [Fact]
    public void Request_completes_when_every_item_is_completed_or_rejected()
        => Assert.Equal(RequestStatus.Completed,
            _workflow.EvaluateRequestStatus(new[] { Item(ItemStatus.Completed), Item(ItemStatus.Rejected) }));

    [Theory]
    [InlineData(ItemStatus.NotStarted, ItemStatus.Completed, true)]
    [InlineData(ItemStatus.WIP, ItemStatus.KIV, true)]
    [InlineData(ItemStatus.Rejected, ItemStatus.WIP, true)]
    [InlineData(ItemStatus.Completed, ItemStatus.WIP, false)]
    [InlineData(ItemStatus.Rejected, ItemStatus.Completed, false)]
    public void Item_transitions_follow_the_state_machine(ItemStatus from, ItemStatus to, bool allowed)
        => Assert.Equal(allowed, _workflow.CanTransition(from, to));

    [Fact]
    public void Illegal_transition_throws()
        => Assert.Throws<InvalidOperationException>(() => _workflow.EnsureItemTransition(ItemStatus.Completed, ItemStatus.NotStarted));
}
