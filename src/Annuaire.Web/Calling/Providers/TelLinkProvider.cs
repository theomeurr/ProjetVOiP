using Annuaire.Web.Options;
using Microsoft.Extensions.Options;

namespace Annuaire.Web.Calling.Providers;

/// <summary>
/// Repli côté client : le serveur renvoie un lien tel: ou sip: que le navigateur ouvre
/// (softphone, Teams, application de téléphonie). Aucun appel réseau côté serveur.
/// </summary>
public sealed class TelLinkProvider(IOptions<CallingOptions> options) : ICallProvider
{
    public string Name => "TelLink";

    public bool RequiresCallerExtension => false;

    public bool RequiresPhoneIp => false;

    public Task<CallResult> DialAsync(CallerInfo caller, string number)
    {
        var config = options.Value.TelLink;
        var link = string.Equals(config.Scheme, "sip", StringComparison.OrdinalIgnoreCase)
            ? string.IsNullOrWhiteSpace(config.SipDomain) ? $"sip:{number}" : $"sip:{number}@{config.SipDomain.Trim()}"
            : $"tel:{number}";

        return Task.FromResult(CallResult.Ok(clientAction: link, detail: link));
    }
}
