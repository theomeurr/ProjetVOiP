using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Annuaire.Web.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    /// <summary>Référence à communiquer à la DSI pour retrouver l'erreur dans le journal.</summary>
    public string? RequestId { get; private set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    public void OnGet(string? @ref)
    {
        // Seuls des caractères sûrs sont affichés (la référence vient de l'URL).
        RequestId = string.IsNullOrEmpty(@ref) ? HttpContext.TraceIdentifier : new string(@ref.Where(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '-' or '.').Take(64).ToArray());
    }
}
