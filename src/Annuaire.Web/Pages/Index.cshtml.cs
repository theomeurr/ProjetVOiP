using Annuaire.Web.Calling;
using Annuaire.Web.Directory;
using Annuaire.Web.Options;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Annuaire.Web.Pages;

public class IndexModel(DirectoryService directory, CallService calls, IOptions<DisplayOptions> display, ILogger<IndexModel> logger) : PageModel
{
    public string OrganizationName => display.Value.OrganizationName;

    /// <summary>Nom Windows brut, affiché si l'agent n'est pas trouvé dans l'annuaire.</summary>
    public string IdentityName { get; private set; } = "";

    /// <summary>Fiche AD de l'utilisateur connecté (null si introuvable ou AD indisponible).</summary>
    public DirectoryEntry? CurrentUser { get; private set; }

    public bool DirectoryAvailable { get; private set; } = true;

    public bool CanCall { get; private set; }

    /// <summary>Raison affichée quand l'appel en un clic est désactivé.</summary>
    public string? CallDisabledReason { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        IdentityName = User.Identity?.Name ?? "";
        try
        {
            CurrentUser = await directory.FindCurrentUserAsync(IdentityName, cancellationToken);
        }
        catch (DirectoryUnavailableException)
        {
            // La page s'affiche quand même ; le script affichera l'erreur de chargement.
            DirectoryAvailable = false;
        }

        if (!DirectoryAvailable)
        {
            CallDisabledReason = "L'annuaire est momentanément indisponible : l'appel en un clic est suspendu.";
            return;
        }

        if (CurrentUser is null)
        {
            logger.LogWarning("Utilisateur {Identity} introuvable dans l'annuaire", IdentityName);
        }

        var status = calls.GetCallerStatus(CurrentUser);
        CanCall = status.CanCall;
        CallDisabledReason = status.Reason;
    }
}
