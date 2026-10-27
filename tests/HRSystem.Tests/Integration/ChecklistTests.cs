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

    /// <summary>An employee in a new department whose head has a login (the final approver).</summary>
    private async Task<(int Leaver, Person Head)> LeaverAsync()
    {
        var (departmentId, head) = await Data.ManagedDepartmentAsync();
        return (await Data.EmployeeAsync(departmentId), head);
    }

    private async Task CompleteAllTasksAsync(string assigneeUserId, int requestId)
    {
        using var assignee = Data.As(assigneeUserId);
        foreach (var task in await TasksOfAsync(assigneeUserId, requestId))
            Assert.True((await assignee.Get<IChecklistService>().UpdateTaskAsync(CompleteAll(task))).Succeeded);
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
        var (leaver, head) = await LeaverAsync();
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

        using (var headScope = Data.As(head.UserId))
            Assert.Contains("not completed", (await headScope.Get<IChecklistService>().FinalizeAsync(requestId)).Error);

        var itemId = await Data.ReadAsync(db => db.AccountRequestItems.Where(i => i.RequestId == accountRequest.Id).Select(i => i.Id).SingleAsync());
        using (var handler = Data.As(itManager.UserId))
            Assert.True((await handler.Get<IAccountRequestService>().UpdateItemAsync(new UpdateItemDto { ItemId = itemId, Status = ItemStatus.Completed })).Succeeded);

        // The final approval belongs to the leaver's department head, not HR.
        using (var hrScope = Data.As(hr.UserId, Permissions.DeparturesManage))
            Assert.Contains("approved by", (await hrScope.Get<IChecklistService>().FinalizeAsync(requestId)).Error);

        using (var headScope = Data.As(head.UserId))
        {
            var finalized = await headScope.Get<IChecklistService>().FinalizeAsync(requestId);
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
        Assert.Contains("No approver", submitted.Error);

        var request = await Data.ReadAsync(db => db.ChecklistRequests.Include(r => r.AccountRequest).Include(r => r.Tasks).SingleAsync(r => r.Id == created.Value));
        Assert.Equal(ChecklistStatus.Draft, request.Status);
        Assert.Empty(request.Tasks);
        Assert.Equal(RequestType.Onboard, request.AccountRequest!.RequestType);
        Assert.Equal(RequestStatus.Draft, request.AccountRequest.Status);
    }

    [SkippableFact]
    public async Task Departure_tasks_go_to_configured_approvers_and_follow_a_change()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var manager = await Data.PersonAsync(null);
        var approver = await Data.PersonAsync(null);
        var successor = await Data.PersonAsync(null);
        await Data.SetTemplateManagersAsync(ChecklistKind.Departure, manager.EmployeeId);

        try
        {
            // Managing departures does not include choosing who approves them.
            using (var departuresOnly = Data.As(hr.UserId, Permissions.DeparturesManage))
            {
                var service = departuresOnly.Get<IChecklistTemplateService>();
                Assert.Empty(await service.GetApproversAsync(ChecklistKind.Departure));
                Assert.False((await service.SetApproversAsync(ChecklistKind.Departure, new Dictionary<int, string?>())).Succeeded);
            }

            using (var settings = Data.As(hr.UserId, Permissions.ChecklistApproversManage))
            {
                var service = settings.Get<IChecklistTemplateService>();
                var rows = await service.GetApproversAsync(ChecklistKind.Departure);
                var set = await service.SetApproversAsync(ChecklistKind.Departure, rows.ToDictionary(r => r.TemplateId, _ => (string?)approver.UserId));
                Assert.True(set.Succeeded, set.Error);
            }

            var requestId = await StartDepartureAsync(hr.UserId, (await LeaverAsync()).Leaver);
            var expected = await Data.ReadAsync(db => db.ChecklistTasks.CountAsync(t => t.ChecklistRequestId == requestId));
            Assert.Equal(expected, (await TasksOfAsync(approver.UserId, requestId)).Count);
            Assert.Empty(await TasksOfAsync(manager.UserId, requestId));

            // Complete one task, then hand the department over: only unfinished tasks move.
            var tasks = await TasksOfAsync(approver.UserId, requestId);
            using (var acting = Data.As(approver.UserId))
                Assert.True((await acting.Get<IChecklistService>().UpdateTaskAsync(CompleteAll(tasks[0]))).Succeeded);

            using (var settings = Data.As(hr.UserId, Permissions.ChecklistApproversManage))
            {
                var service = settings.Get<IChecklistTemplateService>();
                var rows = await service.GetApproversAsync(ChecklistKind.Departure);
                var set = await service.SetApproversAsync(ChecklistKind.Departure, rows.ToDictionary(r => r.TemplateId, _ => (string?)successor.UserId));
                Assert.True(set.Succeeded, set.Error);
            }

            Assert.Equal(expected - 1, (await TasksOfAsync(successor.UserId, requestId)).Count);
            Assert.Empty(await TasksOfAsync(approver.UserId, requestId));
            Assert.Equal(approver.UserId, await Data.ReadAsync(db => db.ChecklistTasks.Where(t => t.Id == tasks[0].Id).Select(t => t.AssignedUserId).SingleAsync()));
        }
        finally
        {
            await Data.SetTemplateManagersAsync(ChecklistKind.Departure, manager.EmployeeId);
        }
    }

    [SkippableFact]
    public async Task Tasks_are_visible_to_their_assignee_only_not_even_to_admins()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var checklistManager = await Data.PersonAsync(null);
        var admin = await Data.PersonAsync(null);
        await Data.SetTemplateManagersAsync(ChecklistKind.Departure, checklistManager.EmployeeId);
        var requestId = await StartDepartureAsync(hr.UserId, (await LeaverAsync()).Leaver);
        var task = (await TasksOfAsync(checklistManager.UserId, requestId)).First();

        using var asAdmin = Data.AsAdmin(admin.UserId);
        var service = asAdmin.Get<IChecklistService>();
        Assert.DoesNotContain(await service.GetMyPendingTasksAsync(), t => t.ChecklistRequestId == requestId);
        Assert.Null(await service.GetTaskAsync(task.Id));
        Assert.False((await service.UpdateTaskAsync(CompleteAll(task))).Succeeded);
    }

    [SkippableFact]
    public async Task Completed_task_moves_to_history_and_can_be_taken_back()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var checklistManager = await Data.PersonAsync(null);
        await Data.SetTemplateManagersAsync(ChecklistKind.Departure, checklistManager.EmployeeId);
        var requestId = await StartDepartureAsync(hr.UserId, (await LeaverAsync()).Leaver);
        var tasks = await TasksOfAsync(checklistManager.UserId, requestId);

        using var manager = Data.As(checklistManager.UserId);
        var service = manager.Get<IChecklistService>();
        foreach (var task in tasks)
            Assert.True((await service.UpdateTaskAsync(CompleteAll(task))).Succeeded);

        Assert.Empty(await TasksOfAsync(checklistManager.UserId, requestId));
        var requestNo = await Data.ReadAsync(db => db.ChecklistRequests.Where(r => r.Id == requestId).Select(r => r.RequestNo).SingleAsync());
        var history = await service.GetMyTaskHistoryAsync(new ChecklistTaskHistoryQuery { Keyword = requestNo, Kind = ChecklistKind.Departure });
        Assert.Equal(tasks.Count, history.Count);
        Assert.All(history, t => Assert.True(t.CanReopen));
        Assert.Equal(ChecklistStatus.PendingFinalReview,
            await Data.ReadAsync(db => db.ChecklistRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync()));

        // A submitted task cannot be edited until it is taken back.
        Assert.Contains("Take it back", (await service.UpdateTaskAsync(CompleteAll(tasks[0]))).Error);
        Assert.True((await service.ReopenTaskAsync(tasks[0].Id)).Succeeded);

        Assert.Single(await TasksOfAsync(checklistManager.UserId, requestId));
        Assert.Equal(ChecklistStatus.InProgress,
            await Data.ReadAsync(db => db.ChecklistRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync()));
    }

    [SkippableFact]
    public async Task Cancelling_a_departure_withdraws_open_tasks_and_the_account_request()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var checklistManager = await Data.PersonAsync(null);
        await Data.SetTemplateManagersAsync(ChecklistKind.Departure, checklistManager.EmployeeId);
        var (itId, _) = await Data.ManagedDepartmentAsync();
        var accountType = await Data.AccountTypeAsync(itId);
        var (leaver, _) = await LeaverAsync();
        await Data.AddHeldAccountAsync(leaver, accountType, "leaver-pc");
        var requestId = await StartDepartureAsync(hr.UserId, leaver, accountType);

        using (var hrScope = Data.As(hr.UserId, Permissions.DeparturesManage))
        {
            var service = hrScope.Get<IChecklistService>();
            Assert.False((await service.CancelAsync(requestId, " ")).Succeeded);
            var cancelled = await service.CancelAsync(requestId, "Employee withdrew resignation");
            Assert.True(cancelled.Succeeded, cancelled.Error);
            Assert.False((await service.CancelAsync(requestId, "again")).Succeeded);
        }

        Assert.Empty(await TasksOfAsync(checklistManager.UserId, requestId));
        var request = await Data.ReadAsync(db => db.ChecklistRequests.Include(r => r.AccountRequest).SingleAsync(r => r.Id == requestId));
        Assert.Equal(ChecklistStatus.Cancelled, request.Status);
        Assert.Equal("Employee withdrew resignation", request.CancelReason);
        Assert.Equal(RequestStatus.Closed, request.AccountRequest!.Status);
        Assert.Equal(EmployeeStatus.Active, await Data.ReadAsync(db => db.Employees.Where(e => e.Id == leaver).Select(e => e.Status).SingleAsync()));

        // The employee can be started on a new departure afterwards.
        using var again = Data.As(hr.UserId, Permissions.DeparturesManage);
        Assert.Null(await again.Get<IChecklistService>().GetOpenRequestIdAsync(ChecklistKind.Departure, leaver));
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
        var requestId = await StartDepartureAsync(hr.UserId, (await LeaverAsync()).Leaver);
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

    [SkippableFact]
    public async Task Department_head_reviews_returns_and_approves_the_departure()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var checklistManager = await Data.PersonAsync(null);
        var admin = await Data.PersonAsync(null);
        await Data.SetTemplateManagersAsync(ChecklistKind.Departure, checklistManager.EmployeeId);
        var (leaver, head) = await LeaverAsync();
        var requestId = await StartDepartureAsync(hr.UserId, leaver);

        using (var headScope = Data.As(head.UserId))
            Assert.DoesNotContain(await headScope.Get<IChecklistService>().GetMyFinalReviewsAsync(), r => r.Id == requestId);

        await CompleteAllTasksAsync(checklistManager.UserId, requestId);
        Assert.Contains(App.Emails.Sent, m => m.To.Contains(head.Email) && m.Subject.Contains("final review"));

        // Nobody but the head sees the final review, administrators included.
        using (var hrScope = Data.As(hr.UserId, Permissions.DeparturesManage))
            Assert.Null(await hrScope.Get<IChecklistService>().GetFinalReviewAsync(requestId));
        using (var adminScope = Data.AsAdmin(admin.UserId))
            Assert.Null(await adminScope.Get<IChecklistService>().GetFinalReviewAsync(requestId));

        int returnedTaskId;
        using (var headScope = Data.As(head.UserId))
        {
            var service = headScope.Get<IChecklistService>();
            Assert.Contains(await service.GetMyFinalReviewsAsync(), r => r.Id == requestId);
            var review = await service.GetFinalReviewAsync(requestId);
            Assert.NotNull(review);
            returnedTaskId = review!.Tasks.First().Id;

            Assert.False((await service.ReturnTaskAsync(returnedTaskId, " ")).Succeeded);
            var returned = await service.ReturnTaskAsync(returnedTaskId, "Laptop serial missing");
            Assert.True(returned.Succeeded, returned.Error);
            Assert.DoesNotContain(await service.GetMyFinalReviewsAsync(), r => r.Id == requestId);
        }

        var back = Assert.Single(await TasksOfAsync(checklistManager.UserId, requestId));
        Assert.Equal(returnedTaskId, back.Id);
        Assert.Equal("Laptop serial missing", back.ReturnReason);
        Assert.Equal(ChecklistStatus.InProgress,
            await Data.ReadAsync(db => db.ChecklistRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync()));

        await CompleteAllTasksAsync(checklistManager.UserId, requestId);
        Assert.Null(await Data.ReadAsync(db => db.ChecklistTasks.Where(t => t.Id == returnedTaskId).Select(t => t.ReturnReason).SingleAsync()));

        using (var headScope = Data.As(head.UserId))
        {
            var approved = await headScope.Get<IChecklistService>().FinalizeAsync(requestId);
            Assert.True(approved.Succeeded, approved.Error);
            var history = await headScope.Get<IChecklistService>().GetMyFinalReviewHistoryAsync(new ChecklistTaskHistoryQuery());
            Assert.Contains(history, r => r.Id == requestId && r.Status == ChecklistStatus.Completed);
        }
        Assert.Equal(EmployeeStatus.Resigned, await Data.ReadAsync(db => db.Employees.Where(e => e.Id == leaver).Select(e => e.Status).SingleAsync()));
    }

    [SkippableFact]
    public async Task HR_finalizes_the_departure_of_a_department_head()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var checklistManager = await Data.PersonAsync(null);
        await Data.SetTemplateManagersAsync(ChecklistKind.Departure, checklistManager.EmployeeId);
        var (_, head) = await Data.ManagedDepartmentAsync();
        var requestId = await StartDepartureAsync(hr.UserId, head.EmployeeId);

        Assert.Null(await Data.ReadAsync(db => db.ChecklistRequests.Where(r => r.Id == requestId).Select(r => r.FinalApproverUserId).SingleAsync()));
        await CompleteAllTasksAsync(checklistManager.UserId, requestId);

        using (var headScope = Data.As(head.UserId))
            Assert.Empty(await headScope.Get<IChecklistService>().GetMyFinalReviewsAsync());
        using var hrScope = Data.As(hr.UserId, Permissions.DeparturesManage);
        var finalized = await hrScope.Get<IChecklistService>().FinalizeAsync(requestId);
        Assert.True(finalized.Succeeded, finalized.Error);
    }

    [SkippableFact]
    public async Task Departure_cannot_be_submitted_without_a_department_head_with_a_login()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var checklistManager = await Data.PersonAsync(null);
        await Data.SetTemplateManagersAsync(ChecklistKind.Departure, checklistManager.EmployeeId);
        var noDepartment = await Data.EmployeeAsync(null);
        var headless = await Data.EmployeeAsync(await Data.DepartmentAsync());

        using var hrScope = Data.As(hr.UserId, Permissions.DeparturesManage);
        var service = hrScope.Get<IChecklistService>();
        foreach (var (leaver, expected) in new[] { (noDepartment, "no department"), (headless, "no manager") })
        {
            var created = await service.CreateDraftAsync(new CreateChecklistRequestDto
            {
                Kind = ChecklistKind.Departure,
                EmployeeId = leaver,
                LastWorkingDate = DateTime.Today,
                LastEmploymentDate = DateTime.Today
            });
            Assert.True(created.Succeeded, created.Error);
            var submitted = await service.SubmitAsync(created.Value);
            Assert.False(submitted.Succeeded);
            Assert.Contains(expected, submitted.Error);
            Assert.Empty(await Data.ReadAsync(db => db.ChecklistTasks.Where(t => t.ChecklistRequestId == created.Value).ToListAsync()));
        }
    }

    [SkippableFact]
    public async Task Departure_items_take_an_attachment_only_where_the_template_allows_it()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var checklistManager = await Data.PersonAsync(null);
        var outsider = await Data.PersonAsync(null);
        await Data.SetTemplateManagersAsync(ChecklistKind.Departure, checklistManager.EmployeeId);
        await Data.SetFirstItemsAllowAttachmentAsync(ChecklistKind.Departure, true);
        try
        {
            var requestId = await StartDepartureAsync(hr.UserId, (await LeaverAsync()).Leaver);
            var task = (await TasksOfAsync(checklistManager.UserId, requestId)).First();
            var withAttachment = task.Items.MinBy(i => i.SortOrder)!;
            Assert.True(withAttachment.AllowAttachment);

            var pdf = "%PDF-1.4\n%test\n"u8.ToArray();
            ChecklistTaskUpdateDto Upload(int itemId, string fileName, byte[] content) => new()
            {
                TaskId = task.Id,
                Items = new() { new ChecklistTaskItemUpdateDto { Id = itemId, NewAttachment = new ChecklistAttachmentUpload(fileName, content) } }
            };

            using var manager = Data.As(checklistManager.UserId);
            var service = manager.Get<IChecklistService>();
            var notAnImage = await service.UpdateTaskAsync(Upload(withAttachment.Id, "photo.png", "hello"u8.ToArray()));
            Assert.Contains("not a JPG, PNG or PDF", notAnImage.Error);
            var withoutAttachment = task.Items.FirstOrDefault(i => !i.AllowAttachment);
            if (withoutAttachment is not null)
                Assert.False((await service.UpdateTaskAsync(Upload(withoutAttachment.Id, "proof.pdf", pdf))).Succeeded);

            var saved = await service.UpdateTaskAsync(Upload(withAttachment.Id, @"C:\scans\proof.pdf", pdf));
            Assert.True(saved.Succeeded, saved.Error);
            var item = (await service.GetTaskAsync(task.Id))!.Items.Single(i => i.Id == withAttachment.Id);
            Assert.Equal("proof.pdf", item.AttachmentFileName);
            Assert.Equal(pdf, await Data.ReadAsync(db => db.ChecklistTaskItems.Where(i => i.Id == item.Id).Select(i => i.Attachment!.Content).SingleAsync()));

            var file = await service.GetItemAttachmentAsync(item.Id);
            Assert.Equal("application/pdf", file!.ContentType);
            Assert.Equal(pdf, file.Content);
            using (var hrScope = Data.As(hr.UserId, Permissions.DeparturesManage))
                Assert.NotNull(await hrScope.Get<IChecklistService>().GetItemAttachmentAsync(item.Id));
            using (var outsiderScope = Data.As(outsider.UserId))
                Assert.Null(await outsiderScope.Get<IChecklistService>().GetItemAttachmentAsync(item.Id));

            var removed = await service.UpdateTaskAsync(new ChecklistTaskUpdateDto
            {
                TaskId = task.Id,
                Items = new() { new ChecklistTaskItemUpdateDto { Id = item.Id, RemoveAttachment = true } }
            });
            Assert.True(removed.Succeeded, removed.Error);
            Assert.Null((await service.GetTaskAsync(task.Id))!.Items.Single(i => i.Id == item.Id).AttachmentFileName);
            Assert.Null(await service.GetItemAttachmentAsync(item.Id));
        }
        finally
        {
            await Data.SetFirstItemsAllowAttachmentAsync(ChecklistKind.Departure, false);
        }
    }

    [SkippableFact]
    public async Task Departure_items_need_exactly_one_of_done_and_na_to_complete()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var checklistManager = await Data.PersonAsync(null);
        await Data.SetTemplateManagersAsync(ChecklistKind.Departure, checklistManager.EmployeeId);
        var requestId = await StartDepartureAsync(hr.UserId, (await LeaverAsync()).Leaver);
        var task = (await TasksOfAsync(checklistManager.UserId, requestId)).First();

        using var manager = Data.As(checklistManager.UserId);
        var service = manager.Get<IChecklistService>();

        var both = CompleteAll(task);
        both.Items[0].IsNotApplicable = true;
        Assert.Contains("not both", (await service.UpdateTaskAsync(both)).Error);
        both.MarkAsCompleted = false;
        Assert.Contains("not both", (await service.UpdateTaskAsync(both)).Error);

        // Every item counts here, optional ones included.
        var oneLeftOpen = CompleteAll(task);
        oneLeftOpen.Items[^1].IsCompleted = false;
        Assert.Contains("either Done or N/A", (await service.UpdateTaskAsync(oneLeftOpen)).Error);

        var mixed = CompleteAll(task);
        mixed.Items[0].IsCompleted = false;
        mixed.Items[0].IsNotApplicable = true;
        var completed = await service.UpdateTaskAsync(mixed);
        Assert.True(completed.Succeeded, completed.Error);
    }
}
