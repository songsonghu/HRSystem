using HRSystem.Application.Interfaces;
using HRSystem.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HRSystem.Application;

/// <summary>Registers Application-layer services into the DI container.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<WorkflowService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IAccountRequestService, AccountRequestService>();
        services.AddScoped<IOffboardingService, OffboardingService>();
        return services;
    }
}
