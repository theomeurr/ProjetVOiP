using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Annuaire.Tests.Api;

/// <summary>Tests de bout en bout sur l'application en mode Development (annuaire simulé).</summary>
public class DirectoryApiTests(DirectoryApiTests.DevelopmentFactory factory) : IClassFixture<DirectoryApiTests.DevelopmentFactory>
{
    [Fact]
    public async Task Directory_endpoint_returns_mock_entries_without_internal_fields()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/directory");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        var entries = json.RootElement.GetProperty("entries");
        Assert.True(entries.GetArrayLength() > 50);
        Assert.Equal("contact", entries[0].GetProperty("kind").GetString());
        Assert.DoesNotContain("phoneIp", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accountName", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("10.20.", body);
        Assert.DoesNotContain("Gérard", body); // compte désactivé
    }

    [Fact]
    public async Task Photo_endpoint_serves_jpeg_and_404_for_unknown_id()
    {
        var client = factory.CreateClient();

        var photo = await client.GetAsync("/api/photo/u-001");
        var missing = await client.GetAsync("/api/photo/inconnu");

        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    public sealed class DevelopmentFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
    }
}
