using Annuaire.Web.Options;
using Microsoft.Extensions.Options;

namespace Annuaire.Web.Calling.Providers;

/// <summary>
/// Postes Yealink : requête Action URI envoyée directement au téléphone de l'appelant,
/// ex. https://10.20.1.15/servlet?key=number=3099. Le poste doit autoriser l'IP du serveur
/// (Action URI Allow IP List). Les identifiants sont gérés par le client HTTP « yealink » (Basic ou Digest).
/// </summary>
public sealed class YealinkActionUriProvider(IHttpClientFactory httpClientFactory, IOptions<CallingOptions> options) : ICallProvider
{
    public const string HttpClientName = "yealink";

    public string Name => "YealinkActionUri";

    public bool RequiresPhoneIp => true;

    public Task<CallResult> DialAsync(CallerInfo caller, string number)
    {
        var template = options.Value.Yealink.UrlTemplate;
        var url = TemplateRenderer.Render(template, TemplateRenderer.Variables(caller, number), TemplateRenderer.UrlEncode);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return Task.FromResult(CallResult.Fail(CallStatus.ProviderError,
                "Le click-to-call est mal configuré. Contactez la DSI.", $"URL Yealink invalide : {url}"));
        }

        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        return HttpCallSender.SendAsync(httpClientFactory.CreateClient(HttpClientName), request, HttpCallSender.TargetPhone);
    }
}
