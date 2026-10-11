using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Annuaire.Tests.Api;

/// <summary>
/// Chaîne complète en mode Development : page → jeton anti-CSRF → POST /api/call → fournisseur Yealink
/// → téléphone simulé. L'utilisateur simulé est Laura MARTIN (poste 4210, IP 10.20.1.246).
/// </summary>
public partial class CallApiTests(CallApiTests.Factory factory) : IClassFixture<CallApiTests.Factory>
{
    [Fact]
    public async Task Call_without_antiforgery_token_is_rejected()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/call", new { entryId = "u-011", field = "ipPhone" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("session a expiré", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Call_with_token_reaches_the_simulated_phone()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var token = await GetTokenAsync(client);

        var response = await PostCallAsync(client, token, """{"entryId":"u-011","field":"ipPhone"}""");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Contains("Votre poste 4210 appelle", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Raw_numbers_or_caller_fields_sent_by_the_browser_are_ignored()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var token = await GetTokenAsync(client);

        // Champs inventés par un client malveillant : ignorés, seul entryId + field compte.
        var response = await PostCallAsync(client, token,
            """{"entryId":"u-011","field":"ipPhone","number":"0899123456","callerIp":"10.0.0.1","callerExt":"9999"}""");
        var message = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("0899123456", message);
        Assert.Contains("Votre poste 4210", message);
    }

    [Theory]
    [InlineData("""{"entryId":"u-011","field":"autre"}""")]
    [InlineData("""{"entryId":"u-011","field":2}""")]
    [InlineData("""{"field":"ipPhone"}""")]
    [InlineData("pas du json")]
    public async Task Malformed_requests_are_rejected(string body)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var token = await GetTokenAsync(client);

        var response = await PostCallAsync(client, token, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<string> GetTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/");
        return TokenPattern().Match(html).Groups[1].Value;
    }

    private static Task<HttpResponseMessage> PostCallAsync(HttpClient client, string token, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/call")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);
        return client.SendAsync(request);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            // Le téléphone simulé est servi par l'application de test elle-même.
            builder.UseSetting("Calling:Yealink:UrlTemplate", "http://localhost/dev/telephone/{callerIp}/servlet?key=number={number}");
            builder.ConfigureServices(services =>
                services.AddHttpClient(Annuaire.Web.Calling.Providers.YealinkActionUriProvider.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => Server.CreateHandler()));
        }
    }
}
