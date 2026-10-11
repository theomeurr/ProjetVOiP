using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Annuaire.Tests.Security;

/// <summary>
/// Accès anonyme refusé, avec la vraie authentification Windows (Negotiate) sur un serveur Kestrel réel :
/// toute ressource (page, API, fichier statique) répond 401 avec une demande d'authentification Negotiate.
/// </summary>
public class AnonymousAccessTests(AnonymousAccessTests.Factory factory) : IClassFixture<AnonymousAccessTests.Factory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/api/directory")]
    [InlineData("/api/photo/u-001")]
    [InlineData("/css/site.css")]
    public async Task Anonymous_requests_are_challenged_with_negotiate(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Negotiate");
    }

    [Fact]
    public async Task Anonymous_call_is_refused()
    {
        var response = await factory.CreateClient().PostAsync("/api/call", new StringContent("""{"entryId":"u-011","field":"ipPhone"}"""));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public Factory()
        {
            UseKestrel(0);
            StartServer();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Staging");
            builder.UseSetting("Directory:Provider", "Mock");
            builder.UseSetting("FileLogging:Directory", System.IO.Directory.CreateTempSubdirectory("annuaire-logs-").FullName);
        }
    }
}

/// <summary>En-têtes de sécurité, pages introuvables, séparation du journal d'audit.</summary>
public partial class HardeningTests(HardeningTests.Factory factory) : IClassFixture<HardeningTests.Factory>
{
    [Fact]
    public async Task Security_headers_are_sent()
    {
        var response = await factory.CreateClient().GetAsync("/");

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Page_has_no_inline_script()
    {
        var html = await factory.CreateClient().GetStringAsync("/");

        Assert.DoesNotMatch(new Regex(@"<script(?![^>]*\bsrc=)[^>]*>", RegexOptions.IgnoreCase), html);
    }

    [Fact]
    public async Task Unknown_api_route_returns_json_404()
    {
        var response = await factory.CreateClient().GetAsync("/api/inexistant");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unknown_page_returns_friendly_404()
    {
        var response = await factory.CreateClient().GetAsync("/nimporte/quoi");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page introuvable", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Calls_are_written_to_the_audit_log_only()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var html = await client.GetStringAsync("/");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/call")
        {
            Content = new StringContent("""{"entryId":"u-011","field":"ipPhone"}""", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);
        await client.SendAsync(request);

        var callLog = await ReadLogAsync("appels-*.log");
        var appLog = await ReadLogAsync("annuaire-*.log");
        Assert.Contains("APPEL", callLog);
        Assert.Contains(@"appelant=DEV\lmartin", callLog);
        Assert.DoesNotContain("APPEL", appLog);
    }

    private async Task<string> ReadLogAsync(string pattern)
    {
        var file = System.IO.Directory.GetFiles(factory.LogDirectory, pattern).Single();
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public string LogDirectory { get; } = System.IO.Directory.CreateTempSubdirectory("annuaire-logs-").FullName;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("FileLogging:Directory", LogDirectory);
            builder.UseSetting("Calling:Provider", "TelLink");
        }
    }
}
