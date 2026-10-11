using Annuaire.Web.Options;
using Annuaire.Web.Security;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Annuaire.Tests.Security;

public class ConfigurationValidatorTests
{
    private static IReadOnlyList<string> Validate(
        string environment = "Production",
        Action<DirectoryOptions>? directory = null,
        Action<LdapOptions>? ldap = null,
        Action<CallingOptions>? calling = null,
        Action<NumberingOptions>? numbering = null)
    {
        var d = new DirectoryOptions();
        var l = new LdapOptions { Server = "dc01.montmagny.local", BaseDn = "DC=montmagny,DC=local", BindUser = "svc", BindPassword = "x" };
        var c = new CallingOptions();
        var n = new NumberingOptions();
        directory?.Invoke(d);
        ldap?.Invoke(l);
        calling?.Invoke(c);
        numbering?.Invoke(n);
        return ConfigurationValidator.Validate(new Env(environment), d, l, c, n);
    }

    [Fact]
    public void Default_production_configuration_with_ldap_settings_is_valid()
    {
        Assert.Empty(Validate());
    }

    [Fact]
    public void Missing_ldap_settings_are_all_reported()
    {
        var errors = Validate(ldap: l => { l.Server = ""; l.BaseDn = ""; l.BindUser = ""; l.BindPassword = ""; });

        Assert.Equal(4, errors.Count);
        Assert.Contains(errors, e => e.Contains("Ldap:BindPassword"));
    }

    [Fact]
    public void Mock_directory_and_plain_ldap_are_refused_in_production()
    {
        Assert.Contains(Validate(directory: d => d.Provider = DirectoryProviderKind.Mock), e => e.Contains("Mock"));
        Assert.Contains(Validate(ldap: l => l.UseSsl = false), e => e.Contains("UseSsl"));
        Assert.Empty(Validate("Development", directory: d => d.Provider = DirectoryProviderKind.Mock));
    }

    [Fact]
    public void Calling_provider_settings_are_checked()
    {
        Assert.Contains(Validate(calling: c => { c.Provider = CallProviderKind.YealinkActionUri; c.Yealink.UrlTemplate = "https://10.0.0.1/x"; }),
            e => e.Contains("{number}"));
        Assert.Contains(Validate(calling: c => { c.Provider = CallProviderKind.GenericHttp; c.GenericHttp.UrlTemplate = ""; }),
            e => e.Contains("GenericHttp:UrlTemplate"));
        Assert.Empty(Validate(calling: c =>
        {
            c.Provider = CallProviderKind.GenericHttp;
            c.GenericHttp.UrlTemplate = "https://ipbx/api/dial?from={callerExt}&to={number}";
        }));
        Assert.Contains(Validate(calling: c => c.AllowedPhoneSubnets = ["10.20.0.0/40"]), e => e.Contains("AllowedPhoneSubnets"));
        Assert.Contains(Validate(calling: c => c.MaxCallsPerMinute = -1), e => e.Contains("MaxCallsPerMinute"));
    }

    [Fact]
    public void Numbering_values_must_be_digits()
    {
        var errors = Validate(numbering: n => { n.OutsideLinePrefix = "0#"; n.InternalExtensionLength = 12; });
        Assert.Equal(2, errors.Count);
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
