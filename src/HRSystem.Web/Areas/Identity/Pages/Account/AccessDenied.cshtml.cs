using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HRSystem.Web.Areas.Identity.Pages.Account;

/// <summary>Shown when an authenticated user lacks permission for the requested resource.</summary>
public class AccessDeniedModel : PageModel
{
    public void OnGet()
    {
    }
}
