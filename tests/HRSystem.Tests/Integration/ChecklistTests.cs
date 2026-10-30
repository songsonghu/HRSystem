using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using HRSystem.Domain.Enums;
using HRSystem.Infrastructure.Identity;
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

    private async Task CompleteAllTasksAsync(string handlerUserId, int requestId)
    {
        using var handler = Data.As(handlerUserId);
        foreach (var task in await TasksOfAsync(handlerUserId, requestId))
            Assert.True((await handler.Get<IChecklistService>().UpdateTaskAsync(CompleteAll(task))).Succeeded);
    }

    private async Task ApproveAllTasksAsync(string approverUserId, int requestId)
    {
        using var approver = Data.As(approverUserId);
        var service = approver.Get<IChecklistService>();
        foreach (var task in (await service.GetMyApprovalTasksAsync()).Where(t => t.ChecklistRequestId == requestId && t.CanApprove))
        {
            var approved = await service.ApproveTaskAsync(task.Id);
            Assert.True(approved.Succeeded, approved.Error);
        }
    }

    /// <summary>The manager handles and approves every department's task while nobody else is set.</summary>
    private async Task CompleteAndApproveAllTasksAsync(string managerUserId, int requestId)
    {
        await CompleteAllTasksAsync(managerUserId, requestId);
        await ApproveAllTasksAsync(managerUserId, requestId);
    }

    private Task<ChecklistStatus> StatusOfAsync(int requestId)
        => Data.ReadAsync(db => db.ChecklistRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync());

    private async Task HeadApprovesAsync(string headUserId, int requestId, string remark = "Handover checked")
    {
        using var head = Data.As(headUserId);
        var approved = await head.Get<IChecklistService>().ApproveByHeadAsync(requestId, remark);
        Assert.True(approved.Succeeded, approved.Error);
    }

    /// <summary>HR / management: anyone holding departures.approve.</summary>
    private Acting AsManagement(string userId) => Data.As(userId, Permissions.DeparturesApprove);

    private Task<List<ChecklistLogAction>> LogActionsAsync(int requestId)
        => Data.ReadAsync(db => db.ChecklistRequestLogs.Where(l => l.ChecklistRequestId == requestId)
            .OrderBy(l => l.At).ThenBy(l => l.Id).Select(l => l.Action).ToListAsync());

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

        // The details page shows the leaver's record and the accounts they still hold.
        using (var page = Data.As(hr.UserId, Permissions.DeparturesManage))
        {
            var details = await page.Get<IChecklistService>().GetAsync(requestId);
            Assert.NotNull(details!.DepartmentName);
            Assert.NotNull(details.JoinDate);
            Assert.NotNull(details.SubmittedByName);
            Assert.Equal(0, details.DaysToLastWorkingDate);
            Assert.Equal("leaver-pc", Assert.Single(details.ActiveAccounts).AccountValue);
        }
        var hrClient = await SignedInClientAsync((await Data.PersonAsync(null, Roles.HR)).Email, TestData.Password);
        var html = await hrClient.GetStringAsync($"/Departures/Details/{requestId}");
        Assert.Contains("Accounts Still Active", html);
        Assert.Contains("leaver-pc", html);

        await CompleteAndApproveAllTasksAsync(checklistManager.UserId, requestId);

        // The head may approve while the accounts are still being disabled; HR / management may not.
        await HeadApprovesAsync(head.UserId, requestId);
        using (var management = AsManagement(hr.UserId))
            Assert.Contains("not completed", (await management.Get<IChecklistService>().ApproveByManagementAsync(requestId, "OK")).Error);

        var itemId = await Data.ReadAsync(db => db.AccountRequestItems.Where(i => i.RequestId == accountRequest.Id).Select(i => i.Id).SingleAsync());
        using (var handler = Data.As(itManager.UserId))
        {
            var service = handler.Get<IAccountRequestService>();
            Assert.True((await service.UpdateItemAsync(new UpdateItemDto { ItemId = itemId, Status = ItemStatus.Completed })).Succeeded);
            Assert.True((await service.ApproveItemAsync(itemId)).Succeeded);
        }

        // departures.manage alone neither finalizes nor approves a departure: that takes departures.approve.
        using (var hrScope = Data.As(hr.UserId, Permissions.DeparturesManage))
        {
            var service = hrScope.Get<IChecklistService>();
            Assert.Contains("Departure Approval", (await service.FinalizeAsync(requestId)).Error);
            Assert.False((await service.ApproveByManagementAsync(requestId, "OK")).Succeeded);
        }

        using (var management = AsManagement(hr.UserId))
        {
            var finalized = await management.Get<IChecklistService>().ApproveByManagementAsync(requestId, "All accounts disabled");
            Assert.True(finalized.Succeeded, finalized.Error);
        }

        Assert.Equal(EmployeeStatus.Resigned, await Data.ReadAsync(db => db.Employees.Where(e => e.Id == leaver).Select(e => e.Status).SingleAsync()));
        Assert.Equal(AccountStatus.Disabled, await Data.ReadAsync(db => db.EmployeeAccounts.Where(a => a.EmployeeId == leaver).Select(a => a.Status).SingleAsync()));
        using (var page = Data.As(hr.UserId, Permissions.DeparturesManage))
        {
            var details = await page.Get<IChecklistService>().GetAsync(requestId);
            Assert.Empty(details!.ActiveAccounts);
            Assert.Null(details.DaysToLastWorkingDate);
        }
    }

    [SkippableFact]
    public async Task Onboarding_only_opens_accounts_and_HR_finalizes_once_they_are_approved()
    {
        RequireDatabase();
        var onboarder = await Data.PersonAsync(null);
        var (itId, itManager) = await Data.ManagedDepartmentAsync();
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
        Assert.True(submitted.Succeeded, submitted.Error);

        // No department checklists any more: only the onboarding account request is dispatched.
        var request = await Data.ReadAsync(db => db.ChecklistRequests.Include(r => r.AccountRequest).Include(r => r.Tasks).SingleAsync(r => r.Id == created.Value));
        Assert.Empty(request.Tasks);
        Assert.Equal(RequestType.Onboard, request.AccountRequest!.RequestType);
        Assert.Equal(RequestStatus.Submitted, request.AccountRequest.Status);
        Assert.True((await service.GetAsync(created.Value))!.CanFinalize);

        // Each finalize attempt runs in its own scope, like a web request, so it reads the current account request.
        async Task<HRSystem.Application.Common.Result> FinalizeAsync()
        {
            using var scope = Data.As(onboarder.UserId, Permissions.OnboardingManage);
            return await scope.Get<IChecklistService>().FinalizeAsync(created.Value);
        }
        Assert.Contains("not completed", (await FinalizeAsync()).Error);

        var itemId = await Data.ReadAsync(db => db.AccountRequestItems.Where(i => i.RequestId == request.AccountRequestId).Select(i => i.Id).SingleAsync());
        using (var it = Data.As(itManager.UserId))
        {
            var accounts = it.Get<IAccountRequestService>();
            Assert.True((await accounts.UpdateItemAsync(new UpdateItemDto { ItemId = itemId, Status = ItemStatus.Completed, AccountValue = "newhire" })).Succeeded);
            Assert.Contains("not completed", (await FinalizeAsync()).Error); // still waiting for approval
            Assert.True((await accounts.ApproveItemAsync(itemId)).Succeeded);
        }

        var finalized = await FinalizeAsync();
        Assert.True(finalized.Succeeded, finalized.Error);
    }

    [SkippableFact]
    public async Task Onboarding_submit_dispatches_nothing_when_a_department_has_no_handler()
    {
        RequireDatabase();
        var onboarder = await Data.PersonAsync(null);
        var accountType = await Data.AccountTypeAsync(await Data.DepartmentAsync()); // department without a manager
        var newHire = await Data.EmployeeAsync(null);

        using var acting = Data.As(onboarder.UserId, Permissions.OnboardingManage);
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
        Assert.Contains("No handler or approver", submitted.Error);

        var request = await Data.ReadAsync(db => db.ChecklistRequests.Include(r => r.AccountRequest).SingleAsync(r => r.Id == created.Value));
        Assert.Equal(ChecklistStatus.Draft, request.Status);
        Assert.Equal(RequestStatus.Draft, request.AccountRequest!.Status);
    }

    [SkippableFact]
    public async Task Departure_tasks_go_to_configured_handlers_and_approvers_and_follow_a_change()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var manager = await Data.PersonAsync(null);
        var handler1 = await Data.PersonAsync(null);
        var handler2 = await Data.PersonAsync(null);
        var approver = await Data.PersonAsync(null);
        var successor = await Data.PersonAsync(null);
        await Data.SetTemplateManagersAsync(ChecklistKind.Departure, manager.EmployeeId);
        var departments = await Data.TemplateDepartmentsAsync(ChecklistKind.Departure);

        async Task SetAsync(params string[] handlers)
        {
            using var settings = Data.As(hr.UserId, Permissions.ChecklistApproversManage);
            var service = settings.Get<IAssignmentService>();
            var rows = new List<DepartmentAssignmentDto>();
            foreach (var departmentId in departments.Distinct())
                rows.Add((await service.GetDepartmentAsync(departmentId, WorkArea.Departure))!);
            foreach (var row in rows)
            {
                row.HandlerUserIds = handlers.ToList();
                row.ApproverUserIds = new List<string> { approver.UserId };
            }
            var saved = await service.SaveSettingsAsync(WorkArea.Departure, rows);
            Assert.True(saved.Succeeded, saved.Error);
        }

        try
        {
            // Managing departures does not include choosing who handles and approves them.
            using (var departuresOnly = Data.As(hr.UserId, Permissions.DeparturesManage))
            {
                var service = departuresOnly.Get<IAssignmentService>();
                Assert.False((await service.SaveSettingsAsync(WorkArea.Departure, Array.Empty<DepartmentAssignmentDto>())).Succeeded);
            }

            await SetAsync(handler1.UserId, handler2.UserId);
            var requestId = await StartDepartureAsync(hr.UserId, (await LeaverAsync()).Leaver);
            var expected = await Data.ReadAsync(db => db.ChecklistTasks.CountAsync(t => t.ChecklistRequestId == requestId));
            Assert.Equal(expected, (await TasksOfAsync(handler1.UserId, requestId)).Count);
            Assert.Equal(expected, (await TasksOfAsync(handler2.UserId, requestId)).Count);
            Assert.Empty(await TasksOfAsync(manager.UserId, requestId));
            Assert.Empty(await TasksOfAsync(approver.UserId, requestId));

            // One handler completes a task; the handlers cannot approve it, the approver can.
            var tasks = await TasksOfAsync(handler1.UserId, requestId);
            using (var h1 = Data.As(handler1.UserId))
            {
                var service = h1.Get<IChecklistService>();
                Assert.True((await service.UpdateTaskAsync(CompleteAll(tasks[0]))).Succeeded);
                Assert.False((await service.ApproveTaskAsync(tasks[0].Id)).Succeeded);
            }
            Assert.Contains(App.Emails.Sent, m => m.To.Contains(approver.Email) && m.Subject.StartsWith("[Approval Required]"));

            using (var a = Data.As(approver.UserId))
            {
                var service = a.Get<IChecklistService>();
                var mine = (await service.GetMyApprovalTasksAsync()).Where(t => t.ChecklistRequestId == requestId).ToList();
                Assert.Equal(expected, mine.Count);
                Assert.True(mine.Single(t => t.Id == tasks[0].Id).CanApprove);
                Assert.All(mine.Where(t => t.Id != tasks[0].Id), t => Assert.True(t.CanEdit && !t.CanApprove));
                Assert.True((await service.ApproveTaskAsync(tasks[0].Id)).Succeeded);
            }

            // Hand the department over: the unfinished tasks show for the new handler at once.
            await SetAsync(successor.UserId);
            Assert.Equal(expected - 1, (await TasksOfAsync(successor.UserId, requestId)).Count);
            Assert.Empty(await TasksOfAsync(handler1.UserId, requestId));
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
    public async Task Completed_task_moves_to_history_and_is_locked_until_the_approver_returns_it()
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
        Assert.All(history, t => Assert.False(t.CanEdit));
        // Completed is not enough for the final review: the department approvals are still missing.
        Assert.Equal(ChecklistStatus.InProgress, await StatusOfAsync(requestId));

        // A completed task cannot be changed by its handlers; only the approver can send it back.
        Assert.Contains("ask the department approver", (await service.UpdateTaskAsync(CompleteAll(tasks[0]))).Error);
        Assert.True((await service.ReturnTaskToHandlersAsync(tasks[0].Id, "Recheck the access card")).Succeeded);
        Assert.Single(await TasksOfAsync(checklistManager.UserId, requestId));

        // Once approved, a task can no longer be returned by its department approver.
        Assert.True((await service.ApproveTaskAsync(tasks[1].Id)).Succeeded);
        Assert.Contains("already approved", (await service.ReturnTaskToHandlersAsync(tasks[1].Id, "too late")).Error);
        Assert.Equal(ChecklistStatus.InProgress, await StatusOfAsync(requestId));
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
    /// All tasks are completed; B reads them while none is approved; A then approves all but one; B
    /// approves the last from its stale view. The request must still reach final review.
    /// </summary>
    [SkippableFact]
    public async Task Parallel_task_approval_reaches_final_review()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var checklistManager = await Data.PersonAsync(null);
        await Data.SetTemplateManagersAsync(ChecklistKind.Departure, checklistManager.EmployeeId);
        var requestId = await StartDepartureAsync(hr.UserId, (await LeaverAsync()).Leaver);
        var tasks = await TasksOfAsync(checklistManager.UserId, requestId);
        Assert.True(tasks.Count >= 2);
        await CompleteAllTasksAsync(checklistManager.UserId, requestId);

        using var b = Data.As(checklistManager.UserId);
        await b.Get<AppDbContext>().ChecklistTasks.Include(t => t.ChecklistRequest).Where(t => t.ChecklistRequestId == requestId).ToListAsync();

        using (var a = Data.As(checklistManager.UserId))
        {
            foreach (var task in tasks.SkipLast(1))
                Assert.True((await a.Get<IChecklistService>().ApproveTaskAsync(task.Id)).Succeeded);
        }

        var last = await b.Get<IChecklistService>().ApproveTaskAsync(tasks.Last().Id);
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

        await CompleteAndApproveAllTasksAsync(checklistManager.UserId, requestId);
        Assert.Contains(App.Emails.Sent, m => m.To.Contains(head.Email) && m.Subject.Contains("final review"));

        // HR without departures.approve cannot open the review; HR / management (admins included) can, but only the head decides now.
        using (var hrScope = Data.As(hr.UserId, Permissions.DeparturesManage))
            Assert.Null(await hrScope.Get<IChecklistService>().GetFinalReviewAsync(requestId));
        using (var adminScope = Data.AsAdmin(admin.UserId))
        {
            var seen = (await adminScope.Get<IChecklistService>().GetFinalReviewAsync(requestId))!;
            Assert.False(seen.CanHeadDecide);
            Assert.False(seen.CanManagementDecide);
            Assert.False((await adminScope.Get<IChecklistService>().ApproveByManagementAsync(requestId, "Too early")).Succeeded);
        }

        int returnedTaskId;
        using (var headScope = Data.As(head.UserId))
        {
            var service = headScope.Get<IChecklistService>();
            Assert.Contains(await service.GetMyFinalReviewsAsync(), r => r.Id == requestId);
            var review = await service.GetFinalReviewAsync(requestId);
            Assert.NotNull(review);
            returnedTaskId = review!.Tasks.First().Id;

            Assert.False((await service.ReturnTasksAsync(requestId, new[] { returnedTaskId }, " ")).Succeeded);
            Assert.False((await service.ReturnTasksAsync(requestId, Array.Empty<int>(), "Laptop serial missing")).Succeeded);
            var returned = await service.ReturnTasksAsync(requestId, new[] { returnedTaskId }, "Laptop serial missing");
            Assert.True(returned.Succeeded, returned.Error);
            Assert.DoesNotContain(await service.GetMyFinalReviewsAsync(), r => r.Id == requestId);
        }

        var back = Assert.Single(await TasksOfAsync(checklistManager.UserId, requestId));
        Assert.Equal(returnedTaskId, back.Id);
        Assert.Equal("Laptop serial missing", back.ReturnReason);
        Assert.Null(back.ApprovedAt); // approved again after the fix
        Assert.Equal(ChecklistStatus.InProgress,
            await Data.ReadAsync(db => db.ChecklistRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync()));

        await CompleteAndApproveAllTasksAsync(checklistManager.UserId, requestId);
        Assert.Null(await Data.ReadAsync(db => db.ChecklistTasks.Where(t => t.Id == returnedTaskId).Select(t => t.ReturnReason).SingleAsync()));

        using (var headScope = Data.As(head.UserId))
        {
            var service = headScope.Get<IChecklistService>();
            Assert.Contains("remark", (await service.ApproveByHeadAsync(requestId, " ")).Error);
            Assert.True((await service.ApproveByHeadAsync(requestId, "Handover complete")).Succeeded);
            Assert.False((await service.ApproveByManagementAsync(requestId, "Not mine")).Succeeded); // the head is not HR / management
        }
        Assert.Equal(ChecklistStatus.PendingManagementApproval, await StatusOfAsync(requestId));
        Assert.Equal(EmployeeStatus.Active, await Data.ReadAsync(db => db.Employees.Where(e => e.Id == leaver).Select(e => e.Status).SingleAsync()));

        using (var management = AsManagement(hr.UserId))
        {
            var service = management.Get<IChecklistService>();
            Assert.Contains(await service.GetMyManagementApprovalsAsync(), r => r.Id == requestId);
            Assert.True((await service.GetFinalReviewAsync(requestId))!.CanManagementDecide);
            Assert.Contains("remark", (await service.ApproveByManagementAsync(requestId, "")).Error);
            var approved = await service.ApproveByManagementAsync(requestId, "Approved by HR");
            Assert.True(approved.Succeeded, approved.Error);
            Assert.Contains(await service.GetMyFinalReviewHistoryAsync(new ChecklistTaskHistoryQuery()), r => r.Id == requestId);
        }
        using (var headScope = Data.As(head.UserId))
        {
            var history = await headScope.Get<IChecklistService>().GetMyFinalReviewHistoryAsync(new ChecklistTaskHistoryQuery());
            Assert.Contains(history, r => r.Id == requestId && r.Status == ChecklistStatus.Completed);
        }
        Assert.Equal(EmployeeStatus.Resigned, await Data.ReadAsync(db => db.Employees.Where(e => e.Id == leaver).Select(e => e.Status).SingleAsync()));
        Assert.Contains(App.Emails.Sent, m => m.To.Contains(head.Email) && m.Subject.StartsWith("[Completed]"));
    }

    [SkippableFact]
    public async Task Management_returns_to_the_head_who_returns_to_departments_and_every_step_is_logged()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var checklistManager = await Data.PersonAsync(null);
        await Data.SetTemplateManagersAsync(ChecklistKind.Departure, checklistManager.EmployeeId);
        var (leaver, head) = await LeaverAsync();
        var requestId = await StartDepartureAsync(hr.UserId, leaver);
        await CompleteAndApproveAllTasksAsync(checklistManager.UserId, requestId);
        await HeadApprovesAsync(head.UserId, requestId, "Looks complete");

        int taskId;
        using (var management = AsManagement(hr.UserId))
        {
            var service = management.Get<IChecklistService>();
            taskId = (await service.GetFinalReviewAsync(requestId))!.Tasks.First().Id;
            // HR / management go back to the head, not straight to departments, when there is a head.
            Assert.False((await service.ReturnTasksAsync(requestId, new[] { taskId }, "Redo")).Succeeded);
            Assert.Contains("reason", (await service.ReturnToHeadAsync(requestId, " ")).Error);
            Assert.True((await service.ReturnToHeadAsync(requestId, "Final pay not confirmed")).Succeeded);
            Assert.DoesNotContain(await service.GetMyManagementApprovalsAsync(), r => r.Id == requestId);
        }
        Assert.Equal(ChecklistStatus.PendingFinalReview, await StatusOfAsync(requestId));
        Assert.Contains(App.Emails.Sent, m => m.To.Contains(head.Email) && m.Subject.StartsWith("[Returned]"));

        using (var headScope = Data.As(head.UserId))
        {
            var service = headScope.Get<IChecklistService>();
            Assert.True((await service.GetFinalReviewAsync(requestId))!.CanHeadDecide);
            Assert.True((await service.ReturnTasksAsync(requestId, new[] { taskId }, "Confirm final pay with Finance")).Succeeded);
        }
        Assert.Equal(ChecklistStatus.InProgress, await StatusOfAsync(requestId));

        await CompleteAllTasksAsync(checklistManager.UserId, requestId);
        using (var approver = Data.As(checklistManager.UserId))
            Assert.True((await approver.Get<IChecklistService>().ApproveTaskAsync(taskId, "Final pay confirmed")).Succeeded);
        await HeadApprovesAsync(head.UserId, requestId, "Fixed");
        using (var management = AsManagement(hr.UserId))
            Assert.True((await management.Get<IChecklistService>().ApproveByManagementAsync(requestId, "Approved")).Succeeded);

        var actions = await LogActionsAsync(requestId);
        Assert.Equal(ChecklistLogAction.Submitted, actions.First());
        Assert.Equal(ChecklistLogAction.ManagementApproved, actions.Last());
        var decisions = actions.Where(a => a is ChecklistLogAction.HeadApproved or ChecklistLogAction.HeadReturned
                                                or ChecklistLogAction.ManagementApproved or ChecklistLogAction.ManagementReturned).ToList();
        Assert.Equal(new[]
        {
            ChecklistLogAction.HeadApproved, ChecklistLogAction.ManagementReturned, ChecklistLogAction.HeadReturned,
            ChecklistLogAction.HeadApproved, ChecklistLogAction.ManagementApproved
        }, decisions);
        Assert.Contains(ChecklistLogAction.TaskCompleted, actions);

        // The page shows the log with each remark and the departments a return went to.
        using var hrPage = Data.As(hr.UserId, Permissions.DeparturesManage);
        var logs = (await hrPage.Get<IChecklistService>().GetAsync(requestId))!.Logs;
        Assert.Contains(logs, l => l.Action == ChecklistLogAction.ManagementReturned && l.Remark == "Final pay not confirmed");
        Assert.Contains(logs, l => l.Action == ChecklistLogAction.HeadReturned && l.Remark == "Confirm final pay with Finance" && l.Departments != null);
        Assert.Contains(logs, l => l.Action == ChecklistLogAction.TaskApproved && l.Remark == "Final pay confirmed");
        Assert.All(logs, l => Assert.False(string.IsNullOrEmpty(l.UserName)));
    }

    [SkippableFact]
    public async Task Final_approver_returns_the_departure_to_several_departments_at_once()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var checklistManager = await Data.PersonAsync(null);
        await Data.SetTemplateManagersAsync(ChecklistKind.Departure, checklistManager.EmployeeId);
        var (leaver, head) = await LeaverAsync();
        var requestId = await StartDepartureAsync(hr.UserId, leaver);
        await CompleteAndApproveAllTasksAsync(checklistManager.UserId, requestId);

        List<int> returnedIds;
        using (var headScope = Data.As(head.UserId))
        {
            var service = headScope.Get<IChecklistService>();
            var review = (await service.GetFinalReviewAsync(requestId))!;
            Assert.True(review.Tasks.Count >= 2);
            returnedIds = review.Tasks.Take(2).Select(t => t.Id).ToList();

            // Another request's task cannot be slipped in.
            Assert.False((await service.ReturnTasksAsync(requestId, returnedIds.Append(int.MaxValue).ToList(), "Missing")).Succeeded);
            var returned = await service.ReturnTasksAsync(requestId, returnedIds, "Handover notes missing");
            Assert.True(returned.Succeeded, returned.Error);
            // Once back with the departments, it cannot be returned again until it comes back for review.
            Assert.False((await service.ReturnTasksAsync(requestId, returnedIds, "Again")).Succeeded);
        }

        var back = await TasksOfAsync(checklistManager.UserId, requestId);
        Assert.Equal(returnedIds.OrderBy(i => i), back.Select(t => t.Id).OrderBy(i => i));
        Assert.All(back, t =>
        {
            Assert.True(t.IsReturned);
            Assert.Equal("Handover notes missing", t.ReturnReason);
            Assert.Equal(head.UserId, t.ReturnedByName);
        });
        Assert.Equal(ChecklistStatus.InProgress, await StatusOfAsync(requestId));

        // The department approver still sees why it was returned when the handlers complete it again.
        await CompleteAllTasksAsync(checklistManager.UserId, requestId);
        using (var approver = Data.As(checklistManager.UserId))
        {
            var resubmitted = (await approver.Get<IChecklistService>().GetTaskAsync(returnedIds[0]))!;
            Assert.True(resubmitted.IsResubmitted);
            Assert.Equal("Handover notes missing", resubmitted.ReturnReason);
        }

        await ApproveAllTasksAsync(checklistManager.UserId, requestId);
        Assert.Equal(ChecklistStatus.PendingFinalReview, await StatusOfAsync(requestId));
        using (var headScope = Data.As(head.UserId))
            Assert.All((await headScope.Get<IChecklistService>().GetFinalReviewAsync(requestId))!.Tasks, t => Assert.Null(t.ReturnReason));
    }

    [SkippableFact]
    public async Task Departure_of_a_department_head_skips_the_head_step()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var checklistManager = await Data.PersonAsync(null);
        await Data.SetTemplateManagersAsync(ChecklistKind.Departure, checklistManager.EmployeeId);
        var (_, head) = await Data.ManagedDepartmentAsync();
        var requestId = await StartDepartureAsync(hr.UserId, head.EmployeeId);

        Assert.Null(await Data.ReadAsync(db => db.ChecklistRequests.Where(r => r.Id == requestId).Select(r => r.FinalApproverUserId).SingleAsync()));
        await CompleteAndApproveAllTasksAsync(checklistManager.UserId, requestId);
        Assert.Equal(ChecklistStatus.PendingManagementApproval, await StatusOfAsync(requestId));

        // The leaver cannot approve their own departure, even holding departures.approve.
        using (var leaverScope = AsManagement(head.UserId))
        {
            var service = leaverScope.Get<IChecklistService>();
            Assert.Empty(await service.GetMyFinalReviewsAsync());
            Assert.DoesNotContain(await service.GetMyManagementApprovalsAsync(), r => r.Id == requestId);
            Assert.Contains("own departure", (await service.ApproveByManagementAsync(requestId, "Me")).Error);
        }

        int taskId;
        using (var management = AsManagement(hr.UserId))
        {
            var service = management.Get<IChecklistService>();
            Assert.Contains("own department", (await service.ReturnToHeadAsync(requestId, "No head")).Error);
            // With no head step, HR / management return to departments themselves.
            taskId = (await service.GetFinalReviewAsync(requestId))!.Tasks.First().Id;
            Assert.True((await service.ReturnTasksAsync(requestId, new[] { taskId }, "Redo the handover")).Succeeded);
        }
        Assert.Equal(ChecklistStatus.InProgress, await StatusOfAsync(requestId));

        await CompleteAndApproveAllTasksAsync(checklistManager.UserId, requestId);
        Assert.Equal(ChecklistStatus.PendingManagementApproval, await StatusOfAsync(requestId));
        using (var management = AsManagement(hr.UserId))
        {
            var finalized = await management.Get<IChecklistService>().ApproveByManagementAsync(requestId, "Approved");
            Assert.True(finalized.Succeeded, finalized.Error);
        }
        Assert.Contains(ChecklistLogAction.ManagementReturned, await LogActionsAsync(requestId));
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

    [SkippableFact]
    public async Task Department_approver_returns_a_completed_task_to_its_handlers()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var manager = await Data.PersonAsync(null);
        var handler = await Data.PersonAsync(null);
        var approver = await Data.PersonAsync(null);
        await Data.SetTemplateManagersAsync(ChecklistKind.Departure, manager.EmployeeId);
        try
        {
            foreach (var departmentId in (await Data.TemplateDepartmentsAsync(ChecklistKind.Departure)).Distinct())
                await Data.SetAssigneesAsync(departmentId, WorkArea.Departure, new[] { handler.UserId }, new[] { approver.UserId });
            var requestId = await StartDepartureAsync(hr.UserId, (await LeaverAsync()).Leaver);
            var task = (await TasksOfAsync(handler.UserId, requestId)).First();

            using (var h = Data.As(handler.UserId))
                Assert.True((await h.Get<IChecklistService>().UpdateTaskAsync(CompleteAll(task))).Succeeded);

            using (var a = Data.As(approver.UserId))
            {
                var service = a.Get<IChecklistService>();
                var view = await service.GetTaskAsync(task.Id);
                Assert.True(view!.CanApprove);
                Assert.False(view.CanEdit); // the approver's Complete is greyed out once the handlers completed it
                Assert.False((await service.ReturnTaskToHandlersAsync(task.Id, " ")).Succeeded);
                Assert.True((await service.ReturnTaskToHandlersAsync(task.Id, "Badge not collected")).Succeeded);
                Assert.False((await service.ApproveTaskAsync(task.Id)).Succeeded); // back with the handlers
            }

            var back = Assert.Single(await TasksOfAsync(handler.UserId, requestId), t => t.Id == task.Id);
            Assert.Equal(ChecklistTaskStatus.Returned, back.Status);
            Assert.Equal("Badge not collected", back.ReturnReason);
            Assert.Equal(approver.UserId, back.ReturnedByName);
            Assert.Contains(App.Emails.Sent, m => m.To.Contains(handler.Email) && m.Subject.StartsWith("[Returned]"));

            // Completed again: the approver still sees the reason, which approval then clears.
            using (var h = Data.As(handler.UserId))
                Assert.True((await h.Get<IChecklistService>().UpdateTaskAsync(CompleteAll(back))).Succeeded);
            using (var a = Data.As(approver.UserId))
            {
                var service = a.Get<IChecklistService>();
                Assert.True((await service.GetTaskAsync(task.Id))!.IsResubmitted);
                Assert.True((await service.ApproveTaskAsync(task.Id)).Succeeded);
                Assert.Null((await service.GetTaskAsync(task.Id))!.ReturnReason);
            }
        }
        finally
        {
            await Data.SetTemplateManagersAsync(ChecklistKind.Departure, manager.EmployeeId);
        }
    }
}
