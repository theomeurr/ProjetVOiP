using System.Net.Http.Headers;
using System.Text;
using Annuaire.Web.Options;
using Microsoft.Extensions.Options;

namespace Annuaire.Web.Calling.Providers;

/// <summary>
/// Fournisseur générique : URL, méthode, corps et en-têtes configurables avec les variables
/// {callerExt}, {callerIp} et {number}. Permet de piloter l'API d'un IPBX (3CX, Wildix, Alcatel…) sans recoder.
/// </summary>
public sealed class GenericHttpProvider(IHttpClientFactory httpClientFactory, IOptions<CallingOptions> options) : ICallProvider
{
    public const string HttpClientName = "generic-pbx";

    public string Name => "GenericHttp";

    /// <summary>L'IP n'est exigée que si un modèle utilise {callerIp}.</summary>
    public bool RequiresPhoneIp
    {
        get
        {
            var config = options.Value.GenericHttp;
            return TemplateRenderer.UsesVariable(config.UrlTemplate, "callerIp")
                   || TemplateRenderer.UsesVariable(config.BodyTemplate, "callerIp")
                   || config.Headers.Values.Any(v => TemplateRenderer.UsesVariable(v, "callerIp"));
        }
    }

    public bool RequiresCallerExtension
    {
        get
        {
            var config = options.Value.GenericHttp;
            return !RequiresPhoneIp
                   || TemplateRenderer.UsesVariable(config.UrlTemplate, "callerExt")
                   || TemplateRenderer.UsesVariable(config.BodyTemplate, "callerExt");
        }
    }

    public Task<CallResult> DialAsync(CallerInfo caller, string number)
    {
        var config = options.Value.GenericHttp;
        var variables = TemplateRenderer.Variables(caller, number);

        var url = TemplateRenderer.Render(config.UrlTemplate, variables, TemplateRenderer.UrlEncode);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return Task.FromResult(CallResult.Fail(CallStatus.ProviderError,
                "Le click-to-call est mal configuré. Contactez la DSI.", $"URL GenericHttp invalide : {url}"));
        }

        var request = new HttpRequestMessage(new HttpMethod(string.IsNullOrWhiteSpace(config.Method) ? "GET" : config.Method.Trim().ToUpperInvariant()), uri);

        if (!string.IsNullOrEmpty(config.BodyTemplate))
        {
            var contentType = string.IsNullOrWhiteSpace(config.ContentType) ? "application/json" : config.ContentType.Trim();
            Func<string, string> encode = contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
                ? TemplateRenderer.JsonEncode
                : contentType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
                    ? TemplateRenderer.UrlEncode
                    : value => value;
            var body = TemplateRenderer.Render(config.BodyTemplate, variables, encode);
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        if (!string.IsNullOrEmpty(config.Username))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        foreach (var (name, value) in config.Headers)
        {
            request.Headers.TryAddWithoutValidation(name, TemplateRenderer.Render(value, variables, v => v));
        }

        return HttpCallSender.SendAsync(httpClientFactory.CreateClient(HttpClientName), request, HttpCallSender.TargetPbx);
    }
}
