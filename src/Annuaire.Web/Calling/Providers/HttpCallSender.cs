using System.Net;

namespace Annuaire.Web.Calling.Providers;

/// <summary>Envoi HTTP commun aux fournisseurs Yealink et générique, avec des messages d'erreur clairs.</summary>
internal static class HttpCallSender
{
    /// <param name="target">« votre téléphone » ou « le serveur de téléphonie », repris dans les messages.</param>
    public static async Task<CallResult> SendAsync(HttpClient client, HttpRequestMessage request, string target)
    {
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var detail = $"{request.Method} {Redact(request.RequestUri)} → {(int)response.StatusCode}";

            if (response.IsSuccessStatusCode)
            {
                return CallResult.Ok(detail: detail);
            }

            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => CallResult.Fail(CallStatus.ProviderError,
                    $"Les identifiants configurés pour {target} sont refusés. Contactez la DSI.", detail),
                HttpStatusCode.Forbidden => CallResult.Fail(CallStatus.ProviderError,
                    $"{Capitalize(target)} refuse la commande d'appel (serveur non autorisé). Contactez la DSI.", detail),
                _ => CallResult.Fail(CallStatus.ProviderError,
                    $"{Capitalize(target)} n'a pas pu lancer l'appel. Réessayez ou composez le numéro.", detail)
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            var reason = ex is HttpRequestException ? ex.Message : "délai dépassé";
            return CallResult.Fail(CallStatus.PhoneUnreachable,
                target == TargetPhone
                    ? "Votre téléphone ne répond pas. Vérifiez qu'il est allumé et branché, puis réessayez."
                    : "Le serveur de téléphonie ne répond pas. Réessayez dans quelques instants.",
                $"{request.Method} {Redact(request.RequestUri)} : {reason}");
        }
    }

    public const string TargetPhone = "votre téléphone";
    public const string TargetPbx = "le serveur de téléphonie";

    /// <summary>Retire d'éventuels identifiants de l'URL avant journalisation.</summary>
    internal static string Redact(Uri? uri)
    {
        if (uri is null)
        {
            return "";
        }

        if (string.IsNullOrEmpty(uri.UserInfo))
        {
            return uri.ToString();
        }

        return new UriBuilder(uri) { UserName = "***", Password = "" }.Uri.ToString();
    }

    private static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
