using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Annuaire.Web.Pages;

public class NotFoundModel : PageModel
{
    public void OnGet() => Response.StatusCode = StatusCodes.Status404NotFound;
}
