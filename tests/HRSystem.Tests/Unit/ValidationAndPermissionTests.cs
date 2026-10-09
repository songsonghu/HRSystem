using System.Security.Claims;
using HRSystem.Application.DTOs;
using HRSystem.Application.DTOs.Validators;
using HRSystem.Application.Security;
using HRSystem.Domain.Enums;
using HRSystem.Infrastructure.Identity;
using HRSystem.Web.Services;

namespace HRSystem.Tests.Unit;

public class EmployeeValidatorTests
{
    private readonly EmployeeEditDtoValidator _validator = new();

    private static EmployeeEditDto Valid() => new()
    {
        EmployeeNo = "hk_001/a",
        Name = "New Hire",
        Gender = Gender.Male,
        DepartmentId = 1,
        JoinDate = DateTime.Today.AddDays(30)
    };

    [Fact]
    public void Future_join_date_and_free_form_staff_no_are_accepted()
        => Assert.True(_validator.Validate(Valid()).IsValid);

    [Fact]
    public void Gender_is_required()
    {
        var dto = Valid();
        dto.Gender = Gender.Unspecified;
        Assert.Contains(_validator.Validate(dto).Errors, e => e.PropertyName == nameof(EmployeeEditDto.Gender));
    }

    [Fact]
    public void Department_is_required()
    {
        var dto = Valid();
        dto.DepartmentId = null;
        Assert.Contains(_validator.Validate(dto).Errors, e => e.PropertyName == nameof(EmployeeEditDto.DepartmentId));
    }

    [Fact]
    public void Resign_date_cannot_precede_join_date()
    {
        var dto = Valid();
        dto.ResignDate = dto.JoinDate.AddDays(-1);
        Assert.Contains(_validator.Validate(dto).Errors, e => e.PropertyName == nameof(EmployeeEditDto.ResignDate));
    }
}

public class PermissionTests
{
    private static ClaimsPrincipal User(string? role = null, params string[] permissions)
    {
        var claims = permissions.Select(p => new Claim(Permissions.ClaimType, p)).ToList();
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public void Permission_names_are_unique()
        => Assert.Equal(Permissions.All.Count, Permissions.All.Select(p => p.Name).Distinct().Count());

    [Fact]
    public void Admin_role_holds_every_permission()
        => Assert.All(Permissions.All, p => Assert.True(User(Roles.Admin).HasPermission(p.Name)));

    [Fact]
    public void Other_users_hold_only_granted_permissions()
    {
        var user = User("HR", Permissions.EmployeesManage);
        Assert.True(user.HasPermission(Permissions.EmployeesManage));
        Assert.False(user.HasPermission(Permissions.UsersManage));
    }
}
