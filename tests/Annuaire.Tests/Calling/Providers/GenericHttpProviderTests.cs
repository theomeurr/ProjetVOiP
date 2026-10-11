using System.Text;
using Annuaire.Web.Calling;
using Annuaire.Web.Calling.Providers;
using Annuaire.Web.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Annuaire.Tests.Calling.Providers;

public class GenericHttpProviderTests
{
    private static readonly CallerInfo Caller = new("lmartin", "Laura MARTIN", "4210", "10.20.1.15");

    private static GenericHttpProvider Create(FakeHttpHandler handler, Action<GenericHttpOptions> configure)
    {
        var options = new CallingOptions();
        configure(options.GenericHttp);
        return new GenericHttpProvider(new FakeHttpClientFactory(handler), MsOptions.Create(options));
    }

    [Fact]
    public async Task Get_template_fills_variables()
    {
        var handler = new FakeHttpHandler();
        var provider = Create(handler, o => o.UrlTemplate = "https://pbx.montmagny.local/api/dial?from={callerExt}&to={number}");

        var result = await provider.DialAsync(Caller, "00612345678");

        Assert.True(result.Success);
        var request = handler.Requests[0].Request;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://pbx.montmagny.local/api/dial?from=4210&to=00612345678", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Post_json_body_headers_and_basic_auth()
    {
        var handler = new FakeHttpHandler();
        var provider = Create(handler, o =>
        {
            o.Method = "post";
            o.UrlTemplate = "https://pbx.montmagny.local/api/calls";
            o.BodyTemplate = """{"from":"{callerExt}","to":"{number}","device":"{callerIp}"}""";
            o.ContentType = "application/json";
            o.Headers = new() { ["X-Api-Key"] = "secret", ["X-Caller"] = "{callerExt}" };
            o.Username = "api";
            o.Password = "motdepasse";
        });

        await provider.DialAsync(Caller, "3099");

        var (request, body) = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("""{"from":"4210","to":"3099","device":"10.20.1.15"}""", body);
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("secret", request.Headers.GetValues("X-Api-Key").Single());
        Assert.Equal("4210", request.Headers.GetValues("X-Caller").Single());
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal("api:motdepasse", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)));
    }

    [Fact]
    public async Task Json_body_values_are_escaped()
    {
        var handler = new FakeHttpHandler();
        var provider = Create(handler, o =>
        {
            o.Method = "POST";
            o.UrlTemplate = "https://pbx/api";
            o.BodyTemplate = """{"from":"{callerExt}"}""";
        });

        await provider.DialAsync(Caller with { Extension = "42\"10" }, "3099");

        Assert.Equal("""{"from":"42\"10"}""", handler.Requests[0].Body);
    }

    [Fact]
    public async Task Form_body_values_are_url_encoded()
    {
        var handler = new FakeHttpHandler();
        var provider = Create(handler, o =>
        {
            o.Method = "POST";
            o.UrlTemplate = "https://pbx/api";
            o.BodyTemplate = "from={callerExt}&to={number}";
            o.ContentType = "application/x-www-form-urlencoded";
        });

        await provider.DialAsync(Caller with { Extension = "42 10" }, "3099");

        Assert.Equal("from=42%2010&to=3099", handler.Requests[0].Body);
    }

    [Fact]
    public void Phone_ip_is_required_only_when_a_template_uses_it()
    {
        Assert.False(Create(new FakeHttpHandler(), o => o.UrlTemplate = "https://pbx/dial?from={callerExt}").RequiresPhoneIp);
        Assert.True(Create(new FakeHttpHandler(), o => o.UrlTemplate = "https://{callerIp}/dial").RequiresPhoneIp);
        Assert.True(Create(new FakeHttpHandler(), o =>
        {
            o.UrlTemplate = "https://pbx/dial";
            o.BodyTemplate = """{"ip":"{callerIp}"}""";
        }).RequiresPhoneIp);
    }

    [Fact]
    public async Task Pbx_unreachable_message_mentions_the_pbx()
    {
        var handler = new FakeHttpHandler(_ => throw new HttpRequestException("Name not resolved"));
        var provider = Create(handler, o => o.UrlTemplate = "https://pbx/dial?to={number}");

        var result = await provider.DialAsync(Caller, "3099");

        Assert.Equal(CallStatus.PhoneUnreachable, result.Status);
        Assert.Contains("serveur de téléphonie", result.Message);
    }
}
