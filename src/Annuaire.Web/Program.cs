using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
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
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using Serilog.Filters;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// ---------- Configuration ----------
var config = builder.Configuration;
builder.Services.Configure<DirectoryOptions>(config.GetSection(DirectoryOptions.SectionName));
builder.Services.Configure<LdapOptions>(config.GetSection(LdapOptions.SectionName));
builder.Services.Configure<DisplayOptions>(config.GetSection(DisplayOptions.SectionName));
builder.Services.Configure<AuthOptions>(config.GetSection(AuthOptions.SectionName));
builder.Services.Configure<CallingOptions>(config.GetSection(CallingOptions.SectionName));
builder.Services.Configure<NumberingOptions>(config.GetSection(NumberingOptions.SectionName));

builder.Services.Configure<FileLoggingOptions>(config.GetSection(FileLoggingOptions.SectionName));

// Les options sont lues à la construction des services (et non ici) : toutes les sources de configuration
// (fichiers, variables d'environnement, surcharges de test) sont alors prises en compte.

// ---------- Journaux (Serilog) ----------
// annuaire-AAAAMMJJ.log : journal technique. appels-AAAAMMJJ.log : journal d'audit des appels (1 an par défaut).
builder.Services.AddSerilog(preserveStaticLogger: true, configureLogger: (services, logger) =>
{
    var fileLogging = services.GetRequiredService<IOptions<FileLoggingOptions>>().Value;
    var logDirectory = ResolveLogDirectory(fileLogging, services.GetRequiredService<IHostEnvironment>());
    logger
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        // Les requêtes vers les téléphones contiennent IP et numéros : elles vont dans le journal d'audit, pas ici.
        .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
        .ReadFrom.Configuration(services.GetRequiredService<IConfiguration>())
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.Logger(general => general
            .Filter.ByExcluding(Matching.FromSource(CallService.AuditLogCategory))
            .WriteTo.File(
                Path.Combine(logDirectory, "annuaire-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: Math.Max(1, fileLogging.AppLogRetentionDays),
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} : {Message:lj}{NewLine}{Exception}"))
        .WriteTo.Logger(audit => audit
            .Filter.ByIncludingOnly(Matching.FromSource(CallService.AuditLogCategory))
            .WriteTo.File(
                Path.Combine(logDirectory, "appels-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: Math.Max(1, fileLogging.CallLogRetentionDays),
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss zzz} {Message:lj}{NewLine}"));
});

// ---------- Authentification ----------
var developmentUser = config[$"{AuthOptions.SectionName}:{nameof(AuthOptions.DevelopmentUser)}"];
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
    // Aucun accès anonyme : toute page, toute API et tout fichier statique exige un utilisateur authentifié.
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
    options.Cookie.Name = "Annuaire.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});

builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));

// ---------- Annuaire ----------
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IDirectoryProvider>(sp => sp.GetRequiredService<IOptions<DirectoryOptions>>().Value.Provider switch
{
    DirectoryProviderKind.Mock => ActivatorUtilities.CreateInstance<MockDirectoryProvider>(sp),
    _ => ActivatorUtilities.CreateInstance<LdapDirectoryProvider>(sp)
});
builder.Services.AddSingleton<DirectoryService>();

// ---------- Click-to-call ----------
builder.Services.AddSingleton<PhoneNumberNormalizer>();
builder.Services.AddSingleton<PhoneSubnetPolicy>();
builder.Services.AddSingleton<CallService>();
builder.Services.AddSingleton<ICallProvider>(sp => sp.GetRequiredService<IOptions<CallingOptions>>().Value.Provider switch
{
    CallProviderKind.YealinkActionUri => ActivatorUtilities.CreateInstance<YealinkActionUriProvider>(sp),
    CallProviderKind.GenericHttp => ActivatorUtilities.CreateInstance<GenericHttpProvider>(sp),
    CallProviderKind.TelLink => ActivatorUtilities.CreateInstance<TelLinkProvider>(sp),
    _ => new DisabledProvider()
});

// Client HTTP vers les téléphones : identifiants Basic/Digest gérés par le handler, pas de redirection suivie.
builder.Services.AddHttpClient(YealinkActionUriProvider.HttpClientName, (sp, client) =>
        client.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<CallingOptions>>().Value.TimeoutSeconds))
    .ConfigurePrimaryHttpMessageHandler(sp =>
    {
        var yealink = sp.GetRequiredService<IOptions<CallingOptions>>().Value.Yealink;
        return CreatePhoneHandler(yealink.Username, yealink.Password, yealink.AcceptInvalidCertificate);
    });

builder.Services.AddHttpClient(GenericHttpProvider.HttpClientName, (sp, client) =>
        client.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<CallingOptions>>().Value.TimeoutSeconds))
    .ConfigurePrimaryHttpMessageHandler(sp => CreatePhoneHandler(
        null, null, sp.GetRequiredService<IOptions<CallingOptions>>().Value.GenericHttp.AcceptInvalidCertificate));

// Limite optionnelle du nombre d'appels par agent (Calling:MaxCallsPerMinute, 0 = pas de limite, par défaut).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(CallEndpoints.RateLimitPolicy, http =>
    {
        var limit = http.RequestServices.GetRequiredService<IOptions<CallingOptions>>().Value.MaxCallsPerMinute;
        var user = http.User.Identity?.Name ?? "anonyme";
        return limit <= 0
            ? RateLimitPartition.GetNoLimiter(user)
            : RateLimitPartition.GetFixedWindowLimiter(user, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
    options.OnRejected = (context, cancellationToken) => new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
        new { success = false, message = "Trop d'appels en peu de temps : patientez une minute avant de réessayer." },
        cancellationToken));
});

// ---------- Web ----------
builder.Services.AddRazorPages();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

// Refuse de démarrer avec une configuration incomplète ou dangereuse (message clair dans le journal d'événements).
var directoryOptions = app.Services.GetRequiredService<IOptions<DirectoryOptions>>().Value;
var callingOptions = app.Services.GetRequiredService<IOptions<CallingOptions>>().Value;
var configurationErrors = ConfigurationValidator.Validate(
    app.Environment,
    directoryOptions,
    app.Services.GetRequiredService<IOptions<LdapOptions>>().Value,
    callingOptions,
    app.Services.GetRequiredService<IOptions<NumberingOptions>>().Value);
if (configurationErrors.Count > 0)
{
    throw new InvalidOperationException(
        "Configuration invalide, l'annuaire ne peut pas démarrer :" + Environment.NewLine + "- "
        + string.Join(Environment.NewLine + "- ", configurationErrors));
}

var subnetPolicy = app.Services.GetRequiredService<PhoneSubnetPolicy>();
if (app.Services.GetRequiredService<ICallProvider>().RequiresPhoneIp && !subnetPolicy.HasRestriction)
{
    app.Logger.LogWarning("Calling:AllowedPhoneSubnets est vide : toute IP lue dans l'AD sera contactée. Renseignez le sous-réseau VoIP dès qu'il est connu.");
}
app.Logger.LogInformation("Annuaire démarré : annuaire {Directory}, fournisseur d'appel {Provider}, journaux dans {LogDirectory}",
    directoryOptions.Provider, callingOptions.Provider,
    ResolveLogDirectory(app.Services.GetRequiredService<IOptions<FileLoggingOptions>>().Value, app.Environment));

// ---------- Pipeline ----------
if (!app.Environment.IsDevelopment())
{
    app.UseFriendlyErrors();
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapDirectoryEndpoints();
app.MapCallEndpoints();
if (app.Environment.IsDevelopment())
{
    app.MapFakePhone();
}
app.MapNotFoundFallbacks();

app.Run();

static string ResolveLogDirectory(FileLoggingOptions options, IHostEnvironment environment) =>
    Path.IsPathRooted(options.Directory) ? options.Directory : Path.Combine(environment.ContentRootPath, options.Directory);

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
