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
        Assert.All(pending.Items, i => Assert.Null(i.AssignedUserId));
        Assert.Contains(App.Emails.Sent, m => m.To.Contains(manager.Email) && m.Subject.Contains("Approval Required"));

        using (var self = Data.As(employee.UserId))
            Assert.False((await self.Get<IAccountRequestService>().ApproveAsync(requestId)).Succeeded);

        using (var approver = Data.As(manager.UserId))
            Assert.True((await approver.Get<IAccountRequestService>().ApproveAsync(requestId)).Succeeded);

        var dispatched = await Data.ReadAsync(db => db.AccountRequests.Include(r => r.Items).SingleAsync(r => r.Id == requestId));
        Assert.Equal(RequestStatus.Submitted, dispatched.Status);
        Assert.Equal(itManager.UserId, Assert.Single(dispatched.Items).AssignedUserId);
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
    public async Task Only_the_assignee_can_process_an_item()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var (itId, itManager) = await Data.ManagedDepartmentAsync();
        var outsider = await Data.PersonAsync(null);
        var employee = await Data.EmployeeAsync(null);
        var requestId = await CreateAndSubmitAsync(hr.UserId, employee, RequestType.Add, new[] { await Data.AccountTypeAsync(itId) }, Permissions.AccountRequestsManage);
        var itemId = await Data.ReadAsync(db => db.AccountRequestItems.Where(i => i.RequestId == requestId).Select(i => i.Id).SingleAsync());

        using (var other = Data.As(outsider.UserId))
            Assert.False((await other.Get<IAccountRequestService>().UpdateItemAsync(new UpdateItemDto { ItemId = itemId, Status = ItemStatus.Completed })).Succeeded);

        using (var assignee = Data.As(itManager.UserId))
            Assert.True((await assignee.Get<IAccountRequestService>().UpdateItemAsync(new UpdateItemDto { ItemId = itemId, Status = ItemStatus.Completed })).Succeeded);

        Assert.Equal(RequestStatus.Completed, await Data.ReadAsync(db => db.AccountRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync()));
    }

    /// <summary>
    /// Reproduces two departments finishing at the same moment: B reads the request while both
    /// items are open, A completes its item, then B completes the other from its stale view.
    /// Without the row-version retry B would save "In Progress" and the request would never complete.
    /// </summary>
    [SkippableFact]
    public async Task Parallel_completion_by_two_departments_completes_the_request()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null);
        var (deptA, handlerA) = await Data.ManagedDepartmentAsync();
        var (deptB, handlerB) = await Data.ManagedDepartmentAsync();
        var typeA = await Data.AccountTypeAsync(deptA);
        var typeB = await Data.AccountTypeAsync(deptB);
        var requestId = await CreateAndSubmitAsync(hr.UserId, await Data.EmployeeAsync(null), RequestType.Add, new[] { typeA, typeB }, Permissions.AccountRequestsManage);
        var items = await Data.ReadAsync(db => db.AccountRequestItems.Where(i => i.RequestId == requestId).ToListAsync());

        using var b = Data.As(handlerB.UserId);
        await b.Get<AppDbContext>().AccountRequestItems.Include(i => i.Request).Where(i => i.RequestId == requestId).ToListAsync();

        using (var a = Data.As(handlerA.UserId))
        {
            var done = await a.Get<IAccountRequestService>().UpdateItemAsync(new UpdateItemDto { ItemId = items.Single(i => i.AccountTypeId == typeA).Id, Status = ItemStatus.Completed });
            Assert.True(done.Succeeded, done.Error);
        }

        var result = await b.Get<IAccountRequestService>().UpdateItemAsync(new UpdateItemDto { ItemId = items.Single(i => i.AccountTypeId == typeB).Id, Status = ItemStatus.Completed });
        Assert.True(result.Succeeded, result.Error);

        Assert.Equal(RequestStatus.Completed, await Data.ReadAsync(db => db.AccountRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync()));
    }
}
