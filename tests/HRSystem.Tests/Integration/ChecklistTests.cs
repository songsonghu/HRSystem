using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using HRSystem.Domain.Enums;
using HRSystem.Infrastructure.Persistence;
using HRSystem.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Tests.Integration;

public class ChecklistTests : IntegrationTest
{
    public ChecklistTests(TestApp app) : base(app) { }

    private static ChecklistTaskUpdateDto CompleteAll(ChecklistTaskDto task) => new()
    {
        TaskId = task.Id,
        MarkAsCompleted = true,
        Items = task.Items.Select(i => new ChecklistTaskItemUpdateDto { Id = i.Id, IsCompleted = true }).ToList()
    };

    private async Task<int> StartDepartureAsync(string hrUserId, int employeeId, params int[] accountTypeIds)
    {
        using var hr = Data.As(hrUserId, Permissions.DeparturesManage);
        var service = hr.Get<IChecklistService>();
        var created = await service.CreateDraftAsync(new CreateChecklistRequestDto
        {
            Kind = ChecklistKind.Departure,
            EmployeeId = employeeId,
            LastWorkingDate = DateTime.Today,
            LastEmploymentDate = DateTime.Today.AddDays(3),
            AccountTypeIds = accountTypeIds.ToList()
        });
        Assert.True(created.Succeeded, created.Error);
        var submitted = await service.SubmitAsync(created.Value);
        Assert.True(submitted.Succeeded, submitted.Error);
        return created.Value;
    }

    private async Task<List<ChecklistTaskDto>> TasksOfAsync(string managerUserId, int requestId)
    {
        using var manager = Data.As(managerUserId);
        return (await manager.Get<IChecklistService>().GetMyPendingTasksAsync()).Where(t => t.ChecklistRequestId == requestId).ToList();
    }

    [SkippableFact]
    public async Task Departure_disables_held_accounts_before_it_can_be_finalized()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var checklistManager = await Data.PersonAsync(null);
        await Data.SetTemplateManagersAsync(ChecklistKind.Departure, checklistManager.EmployeeId);
        var (itId, itManager) = await Data.ManagedDepartmentAsync();
        var accountType = await Data.AccountTypeAsync(itId);
        var leaver = await Data.EmployeeAsync(null);
        await Data.AddHeldAccountAsync(leaver, accountType, "leaver-pc");

        using (var page = Data.As(hr.UserId, Permissions.DeparturesManage))
        {
            var createPage = await page.Get<IChecklistService>().GetCreatePageAsync(ChecklistKind.Departure, leaver);
            Assert.True(createPage.Succeeded, createPage.Error);
            Assert.Equal("leaver-pc", createPage.Value!.ActiveAccounts[accountType]);
        }

        var requestId = await StartDepartureAsync(hr.UserId, leaver, accountType);
        var accountRequest = await Data.ReadAsync(db => db.ChecklistRequests.Where(r => r.Id == requestId).Select(r => r.AccountRequest!).SingleAsync());
        Assert.Equal(RequestType.Offboard, accountRequest.RequestType);
        Assert.Equal(RequestStatus.Submitted, accountRequest.Status);

        using (var manager = Data.As(checklistManager.UserId))
        {
            foreach (var task in await TasksOfAsync(checklistManager.UserId, requestId))
                Assert.True((await manager.Get<IChecklistService>().UpdateTaskAsync(CompleteAll(task))).Succeeded);
        }

        using (var hrScope = Data.As(hr.UserId, Permissions.DeparturesManage))
            Assert.Contains("not completed", (await hrScope.Get<IChecklistService>().FinalizeAsync(requestId)).Error);

        var itemId = await Data.ReadAsync(db => db.AccountRequestItems.Where(i => i.RequestId == accountRequest.Id).Select(i => i.Id).SingleAsync());
        using (var handler = Data.As(itManager.UserId))
            Assert.True((await handler.Get<IAccountRequestService>().UpdateItemAsync(new UpdateItemDto { ItemId = itemId, Status = ItemStatus.Completed })).Succeeded);

        using (var hrScope = Data.As(hr.UserId, Permissions.DeparturesManage))
        {
            var finalized = await hrScope.Get<IChecklistService>().FinalizeAsync(requestId);
            Assert.True(finalized.Succeeded, finalized.Error);
        }

        Assert.Equal(EmployeeStatus.Resigned, await Data.ReadAsync(db => db.Employees.Where(e => e.Id == leaver).Select(e => e.Status).SingleAsync()));
        Assert.Equal(AccountStatus.Disabled, await Data.ReadAsync(db => db.EmployeeAccounts.Where(a => a.EmployeeId == leaver).Select(a => a.Status).SingleAsync()));
    }

    [SkippableFact]
    public async Task Onboarding_submit_dispatches_nothing_when_a_department_cannot_receive_its_task()
    {
        RequireDatabase();
        await Data.SetTemplateManagersAsync(ChecklistKind.Onboarding, null);
        var onboarder = await Data.PersonAsync(null);
        var (itId, _) = await Data.ManagedDepartmentAsync();
        var accountType = await Data.AccountTypeAsync(itId);
        var newHire = await Data.EmployeeAsync(null);

        using var acting = Data.As(onboarder.UserId, Permissions.OnboardingManage); // no account-requests.manage
        var service = acting.Get<IChecklistService>();
        var created = await service.CreateDraftAsync(new CreateChecklistRequestDto
        {
            Kind = ChecklistKind.Onboarding,
            EmployeeId = newHire,
            StartDate = DateTime.Today.AddDays(7),
            AccountTypeIds = { accountType }
        });
        Assert.True(created.Succeeded, created.Error);

        var submitted = await service.SubmitAsync(created.Value);
        Assert.False(submitted.Succeeded);
        Assert.Contains("No manager", submitted.Error);

        var request = await Data.ReadAsync(db => db.ChecklistRequests.Include(r => r.AccountRequest).Include(r => r.Tasks).SingleAsync(r => r.Id == created.Value));
        Assert.Equal(ChecklistStatus.Draft, request.Status);
        Assert.Empty(request.Tasks);
        Assert.Equal(RequestType.Onboard, request.AccountRequest!.RequestType);
        Assert.Equal(RequestStatus.Draft, request.AccountRequest.Status);
    }

    /// <summary>
    /// B reads all department tasks while they are open; A then completes all but one; B completes
    /// the last from its stale view. The request must still reach final review.
    /// </summary>
    [SkippableFact]
    public async Task Parallel_task_completion_reaches_final_review()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var checklistManager = await Data.PersonAsync(null);
        await Data.SetTemplateManagersAsync(ChecklistKind.Departure, checklistManager.EmployeeId);
        var requestId = await StartDepartureAsync(hr.UserId, await Data.EmployeeAsync(null));
        var tasks = await TasksOfAsync(checklistManager.UserId, requestId);
        Assert.True(tasks.Count >= 2);

        using var b = Data.As(checklistManager.UserId);
        await b.Get<AppDbContext>().ChecklistTasks.Include(t => t.ChecklistRequest).Where(t => t.ChecklistRequestId == requestId).ToListAsync();

        using (var a = Data.As(checklistManager.UserId))
        {
            foreach (var task in tasks.SkipLast(1))
                Assert.True((await a.Get<IChecklistService>().UpdateTaskAsync(CompleteAll(task))).Succeeded);
        }

        var last = await b.Get<IChecklistService>().UpdateTaskAsync(CompleteAll(tasks.Last()));
        Assert.True(last.Succeeded, last.Error);

        Assert.Equal(ChecklistStatus.PendingFinalReview,
            await Data.ReadAsync(db => db.ChecklistRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync()));
    }
}
