using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using HRSystem.Domain.Enums;
using HRSystem.Infrastructure.Persistence;
using HRSystem.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Tests.Integration;

public class AccountRequestTests : IntegrationTest
{
    public AccountRequestTests(TestApp app) : base(app) { }

    private async Task<int> CreateAndSubmitAsync(string userId, int employeeId, RequestType type, int[] accountTypeIds, params string[] permissions)
    {
        using var acting = Data.As(userId, permissions);
        var service = acting.Get<IAccountRequestService>();
        var created = await service.CreateAsync(new CreateRequestDto
        {
            EmployeeId = employeeId,
            RequestType = type,
            AccountTypeIds = accountTypeIds.ToList()
        });
        Assert.True(created.Succeeded, created.Error);
        var submitted = await service.SubmitAsync(created.Value);
        Assert.True(submitted.Succeeded, submitted.Error);
        return created.Value;
    }

    [SkippableFact]
    public async Task Self_service_request_needs_the_department_manager_before_dispatch()
    {
        RequireDatabase();
        var (teamId, manager) = await Data.ManagedDepartmentAsync();
        var employee = await Data.PersonAsync(teamId);
        var (itId, itManager) = await Data.ManagedDepartmentAsync();
        var accountType = await Data.AccountTypeAsync(itId);

        var requestId = await CreateAndSubmitAsync(employee.UserId, employee.EmployeeId, RequestType.Add, new[] { accountType });

        var pending = await Data.ReadAsync(db => db.AccountRequests.Include(r => r.Items).SingleAsync(r => r.Id == requestId));
        Assert.Equal(RequestStatus.PendingApproval, pending.Status);
        Assert.Equal(manager.UserId, pending.ApproverUserId);
        Assert.Contains(App.Emails.Sent, m => m.To.Contains(manager.Email) && m.Subject.Contains("Approval Required"));

        using (var self = Data.As(employee.UserId))
            Assert.False((await self.Get<IAccountRequestService>().ApproveAsync(requestId)).Succeeded);

        using (var approver = Data.As(manager.UserId))
            Assert.True((await approver.Get<IAccountRequestService>().ApproveAsync(requestId)).Succeeded);

        var dispatched = await Data.ReadAsync(db => db.AccountRequests.Include(r => r.Items).SingleAsync(r => r.Id == requestId));
        Assert.Equal(RequestStatus.Submitted, dispatched.Status);
        using (var it = Data.As(itManager.UserId))
            Assert.Contains(await it.Get<IAccountRequestService>().GetMyPendingItemsAsync(), i => i.RequestId == requestId);
    }

    [SkippableFact]
    public async Task Employee_cannot_raise_requests_for_others_or_onboarding()
    {
        RequireDatabase();
        var (teamId, _) = await Data.ManagedDepartmentAsync();
        var employee = await Data.PersonAsync(teamId);
        var colleague = await Data.EmployeeAsync(teamId);
        var (itId, _) = await Data.ManagedDepartmentAsync();
        var accountType = await Data.AccountTypeAsync(itId);

        using var acting = Data.As(employee.UserId);
        var service = acting.Get<IAccountRequestService>();
        Assert.False((await service.CreateAsync(new CreateRequestDto { EmployeeId = colleague, RequestType = RequestType.Add, AccountTypeIds = { accountType } })).Succeeded);
        Assert.False((await service.CreateAsync(new CreateRequestDto { EmployeeId = employee.EmployeeId, RequestType = RequestType.Onboard, AccountTypeIds = { accountType } })).Succeeded);
    }

    [SkippableFact]
    public async Task Rejection_needs_a_reason_and_stops_the_request()
    {
        RequireDatabase();
        var (teamId, manager) = await Data.ManagedDepartmentAsync();
        var employee = await Data.PersonAsync(teamId);
        var (itId, _) = await Data.ManagedDepartmentAsync();
        var requestId = await CreateAndSubmitAsync(employee.UserId, employee.EmployeeId, RequestType.Add, new[] { await Data.AccountTypeAsync(itId) });

        using var approver = Data.As(manager.UserId);
        var service = approver.Get<IAccountRequestService>();
        Assert.False((await service.RejectAsync(requestId, " ")).Succeeded);
        Assert.True((await service.RejectAsync(requestId, "Not needed")).Succeeded);

        var rejected = await Data.ReadAsync(db => db.AccountRequests.SingleAsync(r => r.Id == requestId));
        Assert.Equal(RequestStatus.Rejected, rejected.Status);
        Assert.Equal("Not needed", rejected.DecisionRemark);
    }


    [SkippableFact]
    public async Task Only_the_department_manager_processes_and_approves_when_nobody_is_set()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var (itId, itManager) = await Data.ManagedDepartmentAsync();
        var outsider = await Data.PersonAsync(null);
        var employee = await Data.EmployeeAsync(null);
        var accountType = await Data.AccountTypeAsync(itId);
        var requestId = await CreateAndSubmitAsync(hr.UserId, employee, RequestType.Add, new[] { accountType }, Permissions.AccountRequestsManage);
        var itemId = await Data.ReadAsync(db => db.AccountRequestItems.Where(i => i.RequestId == requestId).Select(i => i.Id).SingleAsync());

        using (var other = Data.As(outsider.UserId))
        {
            var service = other.Get<IAccountRequestService>();
            Assert.False((await service.UpdateItemAsync(new UpdateItemDto { ItemId = itemId, Status = ItemStatus.Completed })).Succeeded);
            Assert.Empty(await service.GetMyPendingItemsAsync());
        }

        using (var manager = Data.As(itManager.UserId))
        {
            var service = manager.Get<IAccountRequestService>();
            Assert.Contains(await service.GetMyPendingItemsAsync(), i => i.Id == itemId);
            var done = await service.UpdateItemAsync(new UpdateItemDto { ItemId = itemId, Status = ItemStatus.Completed, AccountValue = "jdoe" });
            Assert.True(done.Succeeded, done.Error);

            // Completed is not the end: the request waits for the department approval.
            Assert.Equal(RequestStatus.InProgress, await Data.ReadAsync(db => db.AccountRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync()));
            Assert.False(await Data.ReadAsync(db => db.EmployeeAccounts.AnyAsync(a => a.EmployeeId == employee && a.AccountTypeId == accountType)));
            Assert.True(Assert.Single(await service.GetMyApprovalItemsAsync(), i => i.Id == itemId).CanApprove);

            using (var other = Data.As(outsider.UserId))
                Assert.False((await other.Get<IAccountRequestService>().ApproveItemAsync(itemId)).Succeeded);

            var approved = await service.ApproveItemAsync(itemId);
            Assert.True(approved.Succeeded, approved.Error);
        }

        Assert.Equal(RequestStatus.Completed, await Data.ReadAsync(db => db.AccountRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync()));
        Assert.True(await Data.ReadAsync(db => db.EmployeeAccounts.AnyAsync(a => a.EmployeeId == employee && a.AccountTypeId == accountType && a.AccountValue == "jdoe")));
    }

    [SkippableFact]
    public async Task Any_handler_processes_and_any_approver_approves_or_returns()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var (itId, itManager) = await Data.ManagedDepartmentAsync();
        var handler1 = await Data.PersonAsync(null);
        var handler2 = await Data.PersonAsync(null);
        var approver1 = await Data.PersonAsync(null);
        var approver2 = await Data.PersonAsync(null);
        await Data.SetAssigneesAsync(itId, WorkArea.Account, new[] { handler1.UserId, handler2.UserId }, new[] { approver1.UserId, approver2.UserId });
        var requestId = await CreateAndSubmitAsync(hr.UserId, await Data.EmployeeAsync(null), RequestType.Add, new[] { await Data.AccountTypeAsync(itId) }, Permissions.AccountRequestsManage);
        var itemId = await Data.ReadAsync(db => db.AccountRequestItems.Where(i => i.RequestId == requestId).Select(i => i.Id).SingleAsync());

        // Both handlers see the item; the manager no longer does once handlers are set.
        foreach (var handler in new[] { handler1, handler2 })
        {
            using var scope = Data.As(handler.UserId);
            Assert.Contains(await scope.Get<IAccountRequestService>().GetMyPendingItemsAsync(), i => i.Id == itemId);
        }
        using (var manager = Data.As(itManager.UserId))
            Assert.False((await manager.Get<IAccountRequestService>().UpdateItemAsync(new UpdateItemDto { ItemId = itemId, Status = ItemStatus.WIP })).Succeeded);

        using (var h1 = Data.As(handler1.UserId))
        {
            var service = h1.Get<IAccountRequestService>();
            Assert.True((await service.UpdateItemAsync(new UpdateItemDto { ItemId = itemId, Status = ItemStatus.Completed })).Succeeded);
            Assert.False((await service.ApproveItemAsync(itemId)).Succeeded); // handlers do not approve
            Assert.False((await service.UpdateItemAsync(new UpdateItemDto { ItemId = itemId, Status = ItemStatus.WIP })).Succeeded); // submitted
        }

        using (var a1 = Data.As(approver1.UserId))
        {
            var service = a1.Get<IAccountRequestService>();
            Assert.False((await service.ReturnItemAsync(itemId, " ")).Succeeded);
            Assert.True((await service.ReturnItemAsync(itemId, "Wrong login name")).Succeeded);
        }
        Assert.Contains(App.Emails.Sent, m => m.To.Contains(handler2.Email) && m.Subject.StartsWith("[Returned]"));

        using (var h2 = Data.As(handler2.UserId))
        {
            var service = h2.Get<IAccountRequestService>();
            var returned = Assert.Single(await service.GetMyPendingItemsAsync(), i => i.Id == itemId);
            Assert.Equal("Wrong login name", returned.ReturnReason);
            Assert.True((await service.UpdateItemAsync(new UpdateItemDto { ItemId = itemId, Status = ItemStatus.Completed, AccountValue = "fixed" })).Succeeded);
        }
        Assert.Contains(App.Emails.Sent, m => m.To.Contains(approver2.Email) && m.Subject.StartsWith("[Approval Required]"));

        using (var a2 = Data.As(approver2.UserId))
            Assert.True((await a2.Get<IAccountRequestService>().ApproveItemAsync(itemId)).Succeeded);

        Assert.Equal(RequestStatus.Completed, await Data.ReadAsync(db => db.AccountRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync()));
    }

    /// <summary>
    /// Reproduces two departments finishing at the same moment: B reads the request while both
    /// items are unapproved, A approves its item, then B approves the other from its stale view.
    /// Without the row-version retry B would save "In Progress" and the request would never complete.
    /// </summary>
    [SkippableFact]
    public async Task Parallel_approval_by_two_departments_completes_the_request()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var (deptA, managerA) = await Data.ManagedDepartmentAsync();
        var (deptB, managerB) = await Data.ManagedDepartmentAsync();
        var typeA = await Data.AccountTypeAsync(deptA);
        var typeB = await Data.AccountTypeAsync(deptB);
        var requestId = await CreateAndSubmitAsync(hr.UserId, await Data.EmployeeAsync(null), RequestType.Add, new[] { typeA, typeB }, Permissions.AccountRequestsManage);
        var items = await Data.ReadAsync(db => db.AccountRequestItems.Where(i => i.RequestId == requestId).ToListAsync());
        var itemA = items.Single(i => i.AccountTypeId == typeA).Id;
        var itemB = items.Single(i => i.AccountTypeId == typeB).Id;

        foreach (var (manager, itemId) in new[] { (managerA, itemA), (managerB, itemB) })
        {
            using var scope = Data.As(manager.UserId);
            var done = await scope.Get<IAccountRequestService>().UpdateItemAsync(new UpdateItemDto { ItemId = itemId, Status = ItemStatus.Completed });
            Assert.True(done.Succeeded, done.Error);
        }
        Assert.Equal(RequestStatus.InProgress, await Data.ReadAsync(db => db.AccountRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync()));

        using var b = Data.As(managerB.UserId);
        await b.Get<AppDbContext>().AccountRequestItems.Include(i => i.Request).Where(i => i.RequestId == requestId).ToListAsync();

        using (var a = Data.As(managerA.UserId))
        {
            var approved = await a.Get<IAccountRequestService>().ApproveItemAsync(itemA);
            Assert.True(approved.Succeeded, approved.Error);
        }

        var result = await b.Get<IAccountRequestService>().ApproveItemAsync(itemB);
        Assert.True(result.Succeeded, result.Error);

        Assert.Equal(RequestStatus.Completed, await Data.ReadAsync(db => db.AccountRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync()));
    }
}
