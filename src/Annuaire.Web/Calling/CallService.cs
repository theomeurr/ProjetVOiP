using System.Security.Claims;
using Annuaire.Web.Api;
using Annuaire.Web.Calling.Providers;
using Annuaire.Web.Directory;
using Annuaire.Web.Options;
using Microsoft.Extensions.Options;

namespace Annuaire.Web.Calling;

/// <summary>Le navigateur ne choisit qu'une fiche et un type de numéro ; jamais le numéro brut ni le poste source.</summary>
public enum CallField
{
    IpPhone,
    TelephoneNumber,
    Mobile
}

/// <summary>Comment l'agent connecté passe ses appels. Tout le monde peut appeler ; seul le moyen change.</summary>
public enum CallMode
{
    /// <summary>Le serveur fait composer le téléphone physique de l'agent (ou l'IPBX).</summary>
    Server,

    /// <summary>Le navigateur ouvre un lien tel:/sip: (fournisseur TelLink, ou repli si l'IP du téléphone est inutilisable).</summary>
    ClientLink,

    /// <summary>Aucun téléphone pilotable : « Appeler » copie le numéro, que l'agent compose lui-même.</summary>
    Manual
}

/// <param name="Mode">Moyen d'appel de l'agent.</param>
/// <param name="Notice">Explication affichée à l'agent quand le mode n'est pas l'appel automatique normal.</param>
/// <param name="Anomaly">Problème de données AD à corriger par la DSI (journalisé, jamais affiché tel quel).</param>
public sealed record CallerStatus(CallMode Mode, string? Notice, string? Anomaly = null);

/// <summary>
/// Orchestration d'un appel : identifie l'appelant (Windows → AD), relit le numéro de la fiche ciblée,
/// le normalise, applique les contrôles de sécurité puis délègue au fournisseur configuré. Chaque demande est journalisée.
/// </summary>
public sealed class CallService(
    DirectoryService directory,
    ICallProvider provider,
    PhoneNumberNormalizer normalizer,
    PhoneSubnetPolicy subnetPolicy,
    IOptions<DisplayOptions> display,
    IOptions<CallingOptions> callingOptions,
    ILoggerFactory loggerFactory)
{
    /// <summary>Catégorie de journal dédiée aux appels (fichier séparé, voir configuration Serilog).</summary>
    public const string AuditLogCategory = "Annuaire.Calls";

    public const string ManualNotice =
        "Aucun poste téléphonique n'est associé à votre compte dans l'annuaire : le bouton « Appeler » copie le numéro, composez-le sur votre téléphone.";

    public const string UnknownUserNotice =
        "Votre compte n'a pas été trouvé dans l'annuaire : le bouton « Appeler » copie le numéro, composez-le sur votre téléphone.";

    public const string DisabledNotice =
        "L'appel automatique n'est pas encore activé : le bouton « Appeler » copie le numéro, composez-le sur votre téléphone.";

    public const string LinkFallbackNotice =
        "L'adresse de votre téléphone n'est pas exploitable dans l'annuaire : vos appels passent par votre application de téléphonie. Signalez-le à la DSI.";

    private readonly ILogger _audit = loggerFactory.CreateLogger(AuditLogCategory);
    private readonly ILogger _logger = loggerFactory.CreateLogger<CallService>();
    private readonly TelLinkProvider _linkFallback = new(callingOptions);

    public CallerStatus GetCallerStatus(DirectoryEntry? user)
    {
        if (!provider.IsEnabled)
        {
            return new(CallMode.Manual, DisabledNotice);
        }

        if (!provider.RequiresCallerExtension && !provider.RequiresPhoneIp)
        {
            // TelLink : aucun prérequis côté AD.
            return new(provider is TelLinkProvider ? CallMode.ClientLink : CallMode.Server, null);
        }

        if (user is null)
        {
            return new(CallMode.Manual, UnknownUserNotice, "compte absent de l'annuaire");
        }

        if (provider.RequiresCallerExtension && string.IsNullOrWhiteSpace(user.IpPhone))
        {
            return new(CallMode.Manual, ManualNotice, "ipPhone non renseigné");
        }

        if (provider.RequiresPhoneIp)
        {
            // Le serveur ne contacte jamais une IP absente ou hors des réseaux autorisés : repli sur un lien tel:.
            if (string.IsNullOrWhiteSpace(user.PhoneIp))
            {
                return new(CallMode.ClientLink, LinkFallbackNotice, "IP du téléphone non renseignée");
            }

            if (!subnetPolicy.IsAllowed(user.PhoneIp))
            {
                return new(CallMode.ClientLink, LinkFallbackNotice, $"IP du téléphone refusée : {user.PhoneIp}");
            }
        }

        return new(CallMode.Server, null);
    }

    public async Task<CallResult> DialAsync(ClaimsPrincipal user, string? entryId, CallField field, CancellationToken cancellationToken)
    {
        var identity = user.Identity?.Name ?? "";
        var context = new CallContext();
        CallResult result;

        try
        {
            var snapshot = await directory.GetSnapshotAsync(cancellationToken);
            var account = AccountName.FromIdentity(identity);
            context.Caller = account is null ? null : snapshot.FindByAccount(account);
            context.Target = string.IsNullOrWhiteSpace(entryId) ? null : snapshot.FindById(entryId);
            result = await DialCoreAsync(context, field);
        }
        catch (DirectoryUnavailableException)
        {
            result = CallResult.Fail(CallStatus.ProviderError, DirectoryEndpoints.UnavailableMessage, "Annuaire indisponible");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Erreur inattendue pendant un appel de {Identity}", identity);
            result = CallResult.Fail(CallStatus.ProviderError, "L'appel n'a pas pu être lancé suite à une erreur interne. Réessayez ou composez le numéro.", ex.GetType().Name);
        }

        Audit(identity, context, field, result);
        return result.Success ? result with { Message = SuccessMessage(context, result) } : result;
    }

    private async Task<CallResult> DialCoreAsync(CallContext context, CallField field)
    {
        var target = context.Target;
        if (target is null)
        {
            return CallResult.Fail(CallStatus.NotFound, "Cette fiche n'existe plus dans l'annuaire. Rechargez la page.");
        }

        if (context.Caller is { } self && target.Id == self.Id)
        {
            return CallResult.Fail(CallStatus.NotAllowed, "Vous ne pouvez pas vous appeler vous-même.");
        }

        var raw = field switch
        {
            CallField.IpPhone => target.IpPhone,
            CallField.TelephoneNumber => target.TelephoneNumber,
            // Un mobile masqué par la configuration ne peut pas non plus être composé.
            CallField.Mobile => DirectoryEntryDto.IsMobileVisible(target, display.Value) ? target.Mobile : null,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(raw))
        {
            return CallResult.Fail(CallStatus.NotFound, "Ce numéro n'est pas disponible pour cette fiche.");
        }

        var status = GetCallerStatus(context.Caller);
        context.Status = status;
        if (status.Anomaly is not null)
        {
            _logger.LogWarning("Click-to-call en mode {Mode} pour {Identity} : {Anomaly}",
                status.Mode, context.Caller?.AccountName ?? "(inconnu)", status.Anomaly);
        }

        if (status.Mode == CallMode.Manual)
        {
            // Normalement géré dans le navigateur (copie du numéro) ; réponse cohérente si l'API est appelée directement.
            return CallResult.Fail(CallStatus.ManualDial, $"Composez le {raw} sur votre téléphone.", status.Anomaly ?? "fournisseur désactivé");
        }

        if (!normalizer.TryNormalize(raw, out var number))
        {
            return CallResult.Fail(CallStatus.InvalidNumber, "Ce numéro n'est pas dans un format composable. Signalez-le à la DSI.", $"Numéro brut : {raw}");
        }

        context.Number = number;
        var caller = context.Caller;
        var callerInfo = new CallerInfo(caller?.AccountName ?? "", caller?.DisplayName ?? "", caller?.IpPhone, caller?.PhoneIp?.Trim());

        if (status.Mode == CallMode.ClientLink && provider is not TelLinkProvider)
        {
            context.ProviderName = $"{_linkFallback.Name} (repli)";
            return await _linkFallback.DialAsync(callerInfo, number.Dialable);
        }

        return await provider.DialAsync(callerInfo, number.Dialable);
    }

    private static string SuccessMessage(CallContext context, CallResult result)
    {
        var target = context.Target!;
        if (result.ClientAction is not null)
        {
            return $"Ouverture de votre application de téléphonie pour appeler {target.DisplayName}…";
        }

        var ext = context.Caller?.IpPhone;
        return string.IsNullOrWhiteSpace(ext)
            ? $"Appel de {target.DisplayName} ({context.Number!.Dialable}) en cours. Décrochez votre téléphone."
            : $"Votre poste {ext} appelle {target.DisplayName} ({context.Number!.Dialable}). Décrochez si le haut-parleur ne s'active pas.";
    }

    private void Audit(string identity, CallContext context, CallField field, CallResult result)
    {
        _audit.Log(
            result.Success ? LogLevel.Information : LogLevel.Warning,
            "APPEL {Result} | appelant={Identity} poste={CallerExt} ip={CallerIp} | destinataire={TargetName} ({TargetId}) champ={Field} numero={Number} | fournisseur={Provider} | {Detail}",
            result.Success ? "OK" : result.Status.ToString(),
            identity,
            context.Caller?.IpPhone ?? "-",
            context.Caller?.PhoneIp ?? "-",
            context.Target?.DisplayName ?? "-",
            context.Target?.Id ?? "-",
            field.ToString(),
            context.Number?.Dialable ?? "-",
            context.ProviderName ?? provider.Name,
            result.Detail ?? result.Message ?? "");
    }

    private sealed class CallContext
    {
        public DirectoryEntry? Caller { get; set; }
        public DirectoryEntry? Target { get; set; }
        public CallerStatus? Status { get; set; }
        public NormalizedNumber? Number { get; set; }
        public string? ProviderName { get; set; }
    }
}
