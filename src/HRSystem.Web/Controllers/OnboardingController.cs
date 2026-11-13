using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using HRSystem.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace HRSystem.Web.Controllers;

[Authorize(Policy = Permissions.OnboardingManage)]
public class OnboardingController : ChecklistRequestsControllerBase
{
    public OnboardingController(IChecklistService service) : base(service) { }

    protected override ChecklistKind Kind => ChecklistKind.Onboarding;
}
