using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using HRSystem.Domain.Enums;
using HRSystem.Infrastructure.Identity;
using HRSystem.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Tests.Integration;

public class ListPagingTests : IntegrationTest
{
    public ListPagingTests(TestApp app) : base(app) { }

    /// <summary>A new department with <paramref name="count"/> employees; returns it and their staff numbers.</summary>
    private async Task<(int DepartmentId, List<int> EmployeeIds)> DepartmentWithEmployeesAsync(int count)
    {
        var departmentId = await Data.DepartmentAsync();
        var ids = new List<int>();
        for (var i = 0; i < count; i++) ids.Add(await Data.EmployeeAsync(departmentId));
        return (departmentId, ids);
    }

    [SkippableFact]
    public async Task Employees_are_searched_by_name_staff_no_and_department_a_page_at_a_time()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null, Roles.HR);
        var (departmentId, ids) = await DepartmentWithEmployeesAsync(12);
        var first = await Data.ReadAsync(db => db.Employees.SingleAsync(e => e.Id == ids[0]));

        using (var scope = Data.As(hr.UserId, Permissions.EmployeesManage))
        {
            var service = scope.Get<IEmployeeService>();
            var page1 = await service.GetPageAsync(new EmployeeListQuery { DepartmentId = departmentId, PageSize = 10 });
            Assert.Equal((12, 2, 10), (page1.TotalCount, page1.TotalPages, page1.Items.Count));
            var page2 = await service.GetPageAsync(new EmployeeListQuery { DepartmentId = departmentId, PageSize = 10, Page = 2 });
            Assert.Equal(2, page2.Items.Count);
            Assert.Empty(page1.Items.Select(e => e.Id).Intersect(page2.Items.Select(e => e.Id)));

            // A page past the end shows the last page; an unknown page size falls back to 10.
            Assert.Equal(2, (await service.GetPageAsync(new EmployeeListQuery { DepartmentId = departmentId, Page = 9 })).Page);
            Assert.Equal(10, (await service.GetPageAsync(new EmployeeListQuery { DepartmentId = departmentId, PageSize = 7 })).PageSize);
            Assert.Single((await service.GetPageAsync(new EmployeeListQuery { DepartmentId = departmentId, PageSize = 50 })).Items, e => e.Id == ids[0]);

            Assert.Equal(ids[0], Assert.Single((await service.GetPageAsync(new EmployeeListQuery { StaffNo = first.EmployeeNo })).Items).Id);
            Assert.Equal(ids[0], Assert.Single((await service.GetPageAsync(new EmployeeListQuery { Name = first.Name.ToUpperInvariant() })).Items).Id);
            Assert.Empty((await service.GetPageAsync(new EmployeeListQuery { Name = first.Name, DepartmentId = departmentId + 100000 })).Items);
        }

        var client = await SignedInClientAsync(hr.Email, TestData.Password);
        var html = await client.GetStringAsync($"/Employees?departmentId={departmentId}&pageSize=10&page=2");
        Assert.Contains("Showing 11–12 of 12", html);
        Assert.Contains("pagination", html);
        Assert.Contains("<option value=\"30\">30</option>", html);
    }

    [SkippableFact]
    public async Task Departure_requests_are_searched_by_employee_and_status_a_page_at_a_time()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null, Roles.HR);
        var (departmentId, ids) = await DepartmentWithEmployeesAsync(3);
        var first = await Data.ReadAsync(db => db.Employees.SingleAsync(e => e.Id == ids[0]));
        var drafts = new List<int>();

        using (var scope = Data.As(hr.UserId, Permissions.DeparturesManage))
        {
            var service = scope.Get<IChecklistService>();
            foreach (var id in ids)
            {
                var draft = await service.CreateDraftAsync(new CreateChecklistRequestDto
                {
                    Kind = ChecklistKind.Departure,
                    EmployeeId = id,
                    LastWorkingDate = DateTime.Today,
                    LastEmploymentDate = DateTime.Today
                });
                Assert.True(draft.Succeeded, draft.Error);
                drafts.Add(draft.Value);
            }

            Task<HRSystem.Application.Common.PagedResult<ChecklistRequestDto>> Search(ChecklistRequestQuery query)
                => service.GetPageAsync(ChecklistKind.Departure, query);

            var inDepartment = await Search(new ChecklistRequestQuery { DepartmentId = departmentId });
            Assert.Equal(drafts.Order(), inDepartment.Items.Select(r => r.Id).Order());
            Assert.Equal(drafts[2], inDepartment.Items[0].Id); // newest first
            Assert.Equal(drafts[0], Assert.Single((await Search(new ChecklistRequestQuery { StaffNo = first.EmployeeNo })).Items).Id);
            Assert.Equal(drafts[0], Assert.Single((await Search(new ChecklistRequestQuery { Name = first.Name.ToUpperInvariant() })).Items).Id);
            Assert.Equal(3, (await Search(new ChecklistRequestQuery { DepartmentId = departmentId, Status = ChecklistStatus.Draft })).TotalCount);
            Assert.Empty((await Search(new ChecklistRequestQuery { DepartmentId = departmentId, Status = ChecklistStatus.Completed })).Items);

            var paged = await Search(new ChecklistRequestQuery { DepartmentId = departmentId, PageSize = 10, Page = 5 });
            Assert.Equal((1, 3), (paged.Page, paged.Items.Count));
        }

        var client = await SignedInClientAsync(hr.Email, TestData.Password);
        var html = await client.GetStringAsync($"/Departures?departmentId={departmentId}&status={(int)ChecklistStatus.Draft}");
        Assert.Contains("3 requests found", html);
        Assert.Contains($"/Departures/Details/{drafts[1]}", html);
        Assert.Contains("<option selected=\"selected\" value=\"0\">Draft</option>", html);
        // Requests are started from the Employees page, not searched for here.
        Assert.DoesNotContain("foundEmployees", html);
        var none = await client.GetStringAsync($"/Departures?departmentId={departmentId}&status={(int)ChecklistStatus.Cancelled}");
        Assert.Contains("No requests match the search.", none);
    }
}
