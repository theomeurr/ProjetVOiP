using System.Security.Claims;
using Annuaire.Web.Api;
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

/// <summary>Peut-on proposer l'appel en un clic à cet utilisateur ? Sinon, pourquoi.</summary>
public sealed record CallerStatus(bool CanCall, string? Reason);

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
    ILoggerFactory loggerFactory)
{
    /// <summary>Catégorie de journal dédiée aux appels (fichier séparé, voir configuration Serilog).</summary>
    public const string AuditLogCategory = "Annuaire.Calls";

    private readonly ILogger _audit = loggerFactory.CreateLogger(AuditLogCategory);
    private readonly ILogger _logger = loggerFactory.CreateLogger<CallService>();

    public CallerStatus GetCallerStatus(DirectoryEntry? user)
    {
        if (!provider.IsEnabled)
        {
            return new(false, DisabledProviderMessage);
        }

        if (user is null)
        {
            return new(false, "Votre compte n'a pas été trouvé dans l'annuaire : l'appel en un clic est désactivé. Contactez la DSI.");
        }

        if (provider.RequiresCallerExtension && string.IsNullOrWhiteSpace(user.IpPhone))
        {
            return new(false, "Aucun poste téléphonique n'est associé à votre compte : l'appel en un clic est désactivé. Vous pouvez copier les numéros, ou demander à la DSI de renseigner votre poste.");
        }

        if (provider.RequiresPhoneIp)
        {
            if (string.IsNullOrWhiteSpace(user.PhoneIp))
            {
                return new(false, "L'adresse de votre téléphone n'est pas renseignée dans l'annuaire : l'appel en un clic est désactivé. Contactez la DSI.");
            }

            if (!subnetPolicy.IsAllowed(user.PhoneIp))
            {
                return new(false, "L'adresse de votre téléphone enregistrée dans l'annuaire n'est pas valide : l'appel en un clic est désactivé. Contactez la DSI.");
            }
        }

        return new(true, null);
    }

    public async Task<CallResult> DialAsync(ClaimsPrincipal user, string? entryId, CallField field, CancellationToken cancellationToken)
    {
        var identity = user.Identity?.Name ?? "";
        DirectoryEntry? caller = null;
        DirectoryEntry? target = null;
        NormalizedNumber? number = null;
        CallResult result;

        try
        {
            var snapshot = await directory.GetSnapshotAsync(cancellationToken);
            var account = AccountName.FromIdentity(identity);
            caller = account is null ? null : snapshot.FindByAccount(account);
            target = string.IsNullOrWhiteSpace(entryId) ? null : snapshot.FindById(entryId);
            result = await DialCoreAsync(caller, target, field, n => number = n);
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

        Audit(identity, caller, target, field, number, result);
        return result.Success ? result with { Message = SuccessMessage(caller!, target!, number!, result) } : result;
    }

    private async Task<CallResult> DialCoreAsync(DirectoryEntry? caller, DirectoryEntry? target, CallField field, Action<NormalizedNumber> captureNumber)
    {
        var status = GetCallerStatus(caller);
        if (!status.CanCall)
        {
            return CallResult.Fail(provider.IsEnabled ? CallStatus.NotAllowed : CallStatus.Disabled, status.Reason!);
        }

        if (target is null)
        {
            return CallResult.Fail(CallStatus.NotFound, "Cette fiche n'existe plus dans l'annuaire. Rechargez la page.");
        }

        if (target.Id == caller!.Id)
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

        if (!normalizer.TryNormalize(raw, out var number))
        {
            return CallResult.Fail(CallStatus.InvalidNumber, "Ce numéro n'est pas dans un format composable. Signalez-le à la DSI.", $"Numéro brut : {raw}");
        }

        captureNumber(number);
        var callerInfo = new CallerInfo(caller.AccountName ?? "", caller.DisplayName, caller.IpPhone, caller.PhoneIp?.Trim());
        return await provider.DialAsync(callerInfo, number.Dialable);
    }

    private static string SuccessMessage(DirectoryEntry caller, DirectoryEntry target, NormalizedNumber number, CallResult result)
    {
        if (result.ClientAction is not null)
        {
            return $"Ouverture de votre application de téléphonie pour appeler {target.DisplayName}…";
        }

        return string.IsNullOrWhiteSpace(caller.IpPhone)
            ? $"Appel de {target.DisplayName} ({number.Dialable}) en cours. Décrochez votre téléphone."
            : $"Votre poste {caller.IpPhone} appelle {target.DisplayName} ({number.Dialable}). Décrochez si le haut-parleur ne s'active pas.";
    }

    private string DisabledProviderMessage => Providers.DisabledProvider.UserMessage;

    private void Audit(string identity, DirectoryEntry? caller, DirectoryEntry? target, CallField field, NormalizedNumber? number, CallResult result)
    {
        _audit.Log(
            result.Success ? LogLevel.Information : LogLevel.Warning,
            "APPEL {Result} | appelant={Identity} poste={CallerExt} ip={CallerIp} | destinataire={TargetName} ({TargetId}) champ={Field} numero={Number} | fournisseur={Provider} | {Detail}",
            result.Success ? "OK" : result.Status.ToString(),
            identity,
            caller?.IpPhone ?? "-",
            caller?.PhoneIp ?? "-",
            target?.DisplayName ?? "-",
            target?.Id ?? "-",
            field,
            number?.Dialable ?? "-",
            provider.Name,
            result.Detail ?? result.Message ?? "");
    }
}
