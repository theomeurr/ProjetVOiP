using Annuaire.Web.Calling;
using Annuaire.Web.Calling.Providers;
using Annuaire.Web.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Annuaire.Tests.Calling.Providers;

public class SimpleProvidersTests
{
    private static readonly CallerInfo Caller = new("lmartin", "Laura MARTIN", null, null);

    [Fact]
    public async Task TelLink_returns_tel_uri_for_the_browser()
    {
        ICallProvider provider = new TelLinkProvider(MsOptions.Create(new CallingOptions()));

        var result = await provider.DialAsync(Caller, "00612345678");

        Assert.True(result.Success);
        Assert.Equal("tel:00612345678", result.ClientAction);
        Assert.False(provider.RequiresCallerExtension);
        Assert.False(provider.RequiresPhoneIp);
    }

    [Theory]
    [InlineData("", "sip:3099")]
    [InlineData("pbx.montmagny.local", "sip:3099@pbx.montmagny.local")]
    public async Task TelLink_supports_sip(string domain, string expected)
    {
        var options = new CallingOptions { TelLink = { Scheme = "SIP", SipDomain = domain } };

        var result = await new TelLinkProvider(MsOptions.Create(options)).DialAsync(Caller, "3099");

        Assert.Equal(expected, result.ClientAction);
    }

    [Fact]
    public async Task Disabled_provider_refuses_every_call()
    {
        ICallProvider provider = new DisabledProvider();

        var result = await provider.DialAsync(Caller, "3099");

        Assert.False(provider.IsEnabled);
        Assert.Equal(CallStatus.Disabled, result.Status);
    }
}
