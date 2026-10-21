using HRSystem.Domain.Enums;

namespace HRSystem.Application.Interfaces;

/// <summary>Numbers and recent activity for the home page, scoped to the current user.</summary>
public interface IDashboardService
{
    Task<DashboardDto> GetAsync(CancellationToken ct = default);
}

public class DashboardDto
{
    public string DisplayName { get; set; } = string.Empty;

    public int MyAccountTasks { get; set; }
    public int MyChecklistTasks { get; set; }
    public int WaitingForMyApproval { get; set; }
    public int MyOpenRequests { get; set; }

    // Only filled when the user holds the matching permission.
    public int? ActiveEmployees { get; set; }
    public int? OpenOnboarding { get; set; }
    public int? OpenDepartures { get; set; }
    public int? OpenAccountRequests { get; set; }

    public IReadOnlyList<DashboardRequestDto> RecentRequests { get; set; } = Array.Empty<DashboardRequestDto>();
}

public record DashboardRequestDto(int Id, string RequestNo, string EmployeeName, RequestType Type, RequestStatus Status, DateTime CreatedAt);
