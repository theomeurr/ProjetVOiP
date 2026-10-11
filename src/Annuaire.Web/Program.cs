using System.Text.Json;
using System.Text.Json.Serialization;
using Annuaire.Web.Api;
using Annuaire.Web.Calling;
using Annuaire.Web.Calling.Providers;
using Annuaire.Web.Dev;
using Annuaire.Web.Directory;
using Annuaire.Web.Options;
using Annuaire.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

// ---------- Configuration ----------
builder.Services.Configure<DirectoryOptions>(builder.Configuration.GetSection(DirectoryOptions.SectionName));
builder.Services.Configure<LdapOptions>(builder.Configuration.GetSection(LdapOptions.SectionName));
builder.Services.Configure<DisplayOptions>(builder.Configuration.GetSection(DisplayOptions.SectionName));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<CallingOptions>(builder.Configuration.GetSection(CallingOptions.SectionName));
builder.Services.Configure<NumberingOptions>(builder.Configuration.GetSection(NumberingOptions.SectionName));

// ---------- Authentification ----------
var developmentUser = builder.Configuration[$"{AuthOptions.SectionName}:{nameof(AuthOptions.DevelopmentUser)}"];
if (builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(developmentUser))
{
    // Utilisateur simulé : permet de développer sur macOS sans Kerberos.
    builder.Services
        .AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(DevelopmentAuthenticationHandler.SchemeName, null);
}
else
{
    // Windows intégrée. Sous IIS, le gestionnaire Negotiate délègue à l'authentification Windows d'IIS.
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
}

builder.Services.AddAuthorization(options =>
{
    // Aucun accès anonyme : toute page et toute API exige un utilisateur authentifié.
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

// ---------- Annuaire ----------
var directoryOptions = builder.Configuration.GetSection(DirectoryOptions.SectionName).Get<DirectoryOptions>() ?? new();
if (directoryOptions.Provider == DirectoryProviderKind.Mock && builder.Environment.IsProduction())
{
    throw new InvalidOperationException(
        "Le fournisseur d'annuaire simulé (Directory:Provider = Mock) est interdit en production.");
}

builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IDirectoryProvider>(sp => directoryOptions.Provider switch
{
    DirectoryProviderKind.Mock => ActivatorUtilities.CreateInstance<MockDirectoryProvider>(sp),
    _ => ActivatorUtilities.CreateInstance<LdapDirectoryProvider>(sp)
});
builder.Services.AddSingleton<DirectoryService>();

// ---------- Click-to-call ----------
var callingOptions = builder.Configuration.GetSection(CallingOptions.SectionName).Get<CallingOptions>() ?? new();
builder.Services.AddSingleton<PhoneNumberNormalizer>();
builder.Services.AddSingleton<PhoneSubnetPolicy>();
builder.Services.AddSingleton<CallService>();
builder.Services.AddSingleton<ICallProvider>(sp => callingOptions.Provider switch
{
    CallProviderKind.YealinkActionUri => ActivatorUtilities.CreateInstance<YealinkActionUriProvider>(sp),
    CallProviderKind.GenericHttp => ActivatorUtilities.CreateInstance<GenericHttpProvider>(sp),
    CallProviderKind.TelLink => ActivatorUtilities.CreateInstance<TelLinkProvider>(sp),
    _ => new DisabledProvider()
});

// Client HTTP vers les téléphones : identifiants Basic/Digest gérés par le handler, pas de redirection suivie.
builder.Services.AddHttpClient(YealinkActionUriProvider.HttpClientName, client =>
        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, callingOptions.TimeoutSeconds)))
    .ConfigurePrimaryHttpMessageHandler(() => CreatePhoneHandler(
        callingOptions.Yealink.Username, callingOptions.Yealink.Password, callingOptions.Yealink.AcceptInvalidCertificate));

builder.Services.AddHttpClient(GenericHttpProvider.HttpClientName, client =>
        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, callingOptions.TimeoutSeconds)))
    .ConfigurePrimaryHttpMessageHandler(() => CreatePhoneHandler(null, null, callingOptions.GenericHttp.AcceptInvalidCertificate));

// ---------- Web ----------
builder.Services.AddRazorPages();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

// Vérifie dès le démarrage la liste des sous-réseaux (erreur claire plutôt qu'au premier appel).
var subnetPolicy = app.Services.GetRequiredService<PhoneSubnetPolicy>();
if (callingOptions.Provider is CallProviderKind.YealinkActionUri && !subnetPolicy.HasRestriction)
{
    app.Logger.LogWarning("Calling:AllowedPhoneSubnets est vide : toute IP lue dans l'AD sera contactée. Renseignez le sous-réseau VoIP.");
}
app.Logger.LogInformation("Fournisseur d'appel : {Provider}", callingOptions.Provider);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapDirectoryEndpoints();
app.MapCallEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapFakePhone();
}

app.Run();

static SocketsHttpHandler CreatePhoneHandler(string? username, string? password, bool acceptInvalidCertificate)
{
    var handler = new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };
    if (!string.IsNullOrEmpty(username))
    {
        handler.Credentials = new System.Net.NetworkCredential(username, password);
    }

    if (acceptInvalidCertificate)
    {
        // Certificats auto-signés des postes. Le risque est limité par Calling:AllowedPhoneSubnets.
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
    }

    return handler;
}

/// <summary>Exposé pour les tests d'intégration.</summary>
public partial class Program;
