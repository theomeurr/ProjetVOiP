using System.Net;

namespace Annuaire.Tests.Calling.Providers;

/// <summary>Handler HTTP simulé : enregistre les requêtes et renvoie une réponse programmée.</summary>
internal sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage>? respond = null) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        return respond?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.OK);
    }
}

internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
