using HRSystem.Domain.Entities;
using HRSystem.Domain.Enums;
using HRSystem.Infrastructure.Identity;
using HRSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRSystem.Tests.Infrastructure;

public sealed record Person(int EmployeeId, string UserId, string Email);

/// <summary>Creates uniquely named data so tests sharing one database never collide.</summary>
public sealed class TestData
{
    public const string Password = "Test-Passw0rd";
    private static int _sequence;
    private readonly TestApp _app;

    public TestData(TestApp app) => _app = app;

    public static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _sequence)}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 14, 48)];

    /// <summary>A DI scope whose services act as the given user.</summary>
    public Acting As(string userId, params string[] permissions)
    {
        var scope = _app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>().ActAs(userId, permissions);
        return new Acting(scope);
    }

    public async Task<int> DepartmentAsync()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var department = new Department { Name = Unique("Dept") };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        return department.Id;
    }

    public async Task<int> EmployeeAsync(int? departmentId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var employee = new Employee
        {
            EmployeeNo = Unique("E"),
            Name = Unique("Employee"),
            Gender = Gender.Female,
            DepartmentId = departmentId,
            JoinDate = DateTime.Today.AddYears(-1),
            Email = $"{Guid.NewGuid():N}@test.local"
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    public async Task<Person> PersonAsync(int? departmentId, params string[] roles)
    {
        var employeeId = await EmployeeAsync(departmentId);
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        var created = await users.CreateAsync(user, Password);
        if (!created.Succeeded) throw new InvalidOperationException(string.Join(" ", created.Errors.Select(e => e.Description)));
        if (roles.Length > 0) await users.AddToRolesAsync(user, roles);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var employee = await db.Employees.SingleAsync(e => e.Id == employeeId);
        employee.UserId = user.Id;
        await db.SaveChangesAsync();
        return new Person(employeeId, user.Id, email);
    }

    /// <summary>A new department whose manager is a new employee with a login.</summary>
    public async Task<(int DepartmentId, Person Manager)> ManagedDepartmentAsync()
    {
        var departmentId = await DepartmentAsync();
        var manager = await PersonAsync(departmentId);
        await SetManagerAsync(departmentId, manager.EmployeeId);
        return (departmentId, manager);
    }

    public async Task SetManagerAsync(int departmentId, int? employeeId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var department = await db.Departments.SingleAsync(d => d.Id == departmentId);
        department.ManagerEmployeeId = employeeId;
        await db.SaveChangesAsync();
    }

    /// <summary>Make <paramref name="employeeId"/> the manager of every department with an active template of this kind.</summary>
    public async Task SetTemplateManagersAsync(ChecklistKind kind, int? employeeId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var departmentIds = await db.ChecklistTemplates.Where(t => t.Kind == kind && t.IsActive).Select(t => t.DepartmentId).ToListAsync();
        foreach (var department in await db.Departments.Where(d => departmentIds.Contains(d.Id)).ToListAsync())
            department.ManagerEmployeeId = employeeId;
        await db.SaveChangesAsync();
    }

    public async Task<int> AccountTypeAsync(int responsibleDepartmentId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var accountType = new AccountType
        {
            Code = Unique("AT"),
            Name = Unique("Account"),
            ResponsibleDeptId = responsibleDepartmentId,
            Audience = AccountTypeAudience.Both,
            IsActive = true
        };
        db.AccountTypes.Add(accountType);
        await db.SaveChangesAsync();
        return accountType.Id;
    }

    public async Task AddHeldAccountAsync(int employeeId, int accountTypeId, string value)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.EmployeeAccounts.Add(new EmployeeAccount { EmployeeId = employeeId, AccountTypeId = accountTypeId, AccountValue = value });
        await db.SaveChangesAsync();
    }

    public async Task<T> ReadAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = _app.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

public sealed class Acting : IDisposable
{
    private readonly IServiceScope _scope;

    public Acting(IServiceScope scope) => _scope = scope;

    public T Get<T>() where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

    public void Dispose() => _scope.Dispose();
}
