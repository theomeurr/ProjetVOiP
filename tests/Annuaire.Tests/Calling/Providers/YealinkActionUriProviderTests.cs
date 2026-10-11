using System.Net;
using Annuaire.Web.Calling;
using Annuaire.Web.Calling.Providers;
using Annuaire.Web.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Annuaire.Tests.Calling.Providers;

public class YealinkActionUriProviderTests
{
    private static readonly CallerInfo Caller = new("lmartin", "Laura MARTIN", "4210", "10.20.1.15");

    private static YealinkActionUriProvider Create(FakeHttpHandler handler, string? template = null)
    {
        var options = new CallingOptions();
        if (template is not null)
        {
            options.Yealink.UrlTemplate = template;
        }

        return new YealinkActionUriProvider(new FakeHttpClientFactory(handler), MsOptions.Create(options));
    }

    [Fact]
    public async Task Sends_action_uri_to_the_caller_phone()
    {
        var handler = new FakeHttpHandler();

        var result = await Create(handler).DialAsync(Caller, "00612345678");

        Assert.True(result.Success);
        var request = Assert.Single(handler.Requests).Request;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://10.20.1.15/servlet?key=number=00612345678", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Template_variables_are_url_encoded()
    {
        var handler = new FakeHttpHandler();
        var caller = Caller with { PhoneIp = "10.20.1.15" };

        await Create(handler, "https://{callerIp}/servlet?key=number={number}&ext={callerExt}").DialAsync(caller with { Extension = "42&x=1" }, "3099");

        Assert.Equal("https://10.20.1.15/servlet?key=number=3099&ext=42%26x%3D1", handler.Requests[0].Request.RequestUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "identifiants")]
    [InlineData(HttpStatusCode.Forbidden, "refuse")]
    [InlineData(HttpStatusCode.InternalServerError, "n'a pas pu")]
    public async Task Phone_errors_become_clear_messages(HttpStatusCode status, string expected)
    {
        var handler = new FakeHttpHandler(_ => new HttpResponseMessage(status));

        var result = await Create(handler).DialAsync(Caller, "3099");

        Assert.Equal(CallStatus.ProviderError, result.Status);
        Assert.Contains(expected, result.Message);
        Assert.Contains(((int)status).ToString(), result.Detail);
    }

    [Fact]
    public async Task Unreachable_phone_is_reported()
    {
        var handler = new FakeHttpHandler(_ => throw new HttpRequestException("Connection refused"));

        var result = await Create(handler).DialAsync(Caller, "3099");

        Assert.Equal(CallStatus.PhoneUnreachable, result.Status);
        Assert.Contains("Votre téléphone ne répond pas", result.Message);
    }

    [Fact]
    public async Task Timeout_is_reported_as_unreachable()
    {
        var handler = new FakeHttpHandler(_ => throw new TaskCanceledException());

        var result = await Create(handler).DialAsync(Caller, "3099");

        Assert.Equal(CallStatus.PhoneUnreachable, result.Status);
    }

    [Fact]
    public async Task Invalid_template_is_a_configuration_error()
    {
        var result = await Create(new FakeHttpHandler(), "file:///{callerIp}").DialAsync(Caller, "3099");

        Assert.Equal(CallStatus.ProviderError, result.Status);
        Assert.Contains("mal configuré", result.Message);
    }
}
