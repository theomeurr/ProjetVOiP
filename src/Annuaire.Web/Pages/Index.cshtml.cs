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

    /// <summary>Moyen d'appel de l'agent (automatique, lien tel:, ou numéro à composer soi-même).</summary>
    public CallMode CallMode { get; private set; } = CallMode.Manual;

    /// <summary>Explication affichée quand l'appel n'est pas entièrement automatique.</summary>
    public string? CallNotice { get; private set; }

    public string CallModeAttribute => CallMode switch
    {
        CallMode.Server => "server",
        CallMode.ClientLink => "link",
        _ => "manual"
    };

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
            CallNotice = "L'annuaire est momentanément indisponible : le bouton « Appeler » copie le numéro, composez-le sur votre téléphone.";
            return;
        }

        if (CurrentUser is null)
        {
            logger.LogWarning("Utilisateur {Identity} introuvable dans l'annuaire", IdentityName);
        }

        var status = calls.GetCallerStatus(CurrentUser);
        CallMode = status.Mode;
        CallNotice = status.Notice;
    }
}
