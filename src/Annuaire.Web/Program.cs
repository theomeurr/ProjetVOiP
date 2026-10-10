using System.Text.Json;
using System.Text.Json.Serialization;
using Annuaire.Web.Api;
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

// ---------- Web ----------
builder.Services.AddRazorPages();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

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

app.Run();

/// <summary>Exposé pour les tests d'intégration.</summary>
public partial class Program;
