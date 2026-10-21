using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using HRSystem.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace HRSystem.Web.Controllers;

[Authorize(Policy = Permissions.DeparturesManage)]
public class DeparturesController : ChecklistRequestsControllerBase
{
    public DeparturesController(IChecklistService service) : base(service) { }

    protected override ChecklistKind Kind => ChecklistKind.Departure;
}
