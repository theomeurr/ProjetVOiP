namespace Annuaire.Web.Dev;

/// <summary>
/// Téléphone Yealink simulé, pour tester toute la chaîne click-to-call sur un poste de développement.
/// Enregistré uniquement en environnement Development (voir Program.cs).
/// </summary>
public static class FakePhoneEndpoints
{
    public static IEndpointRouteBuilder MapFakePhone(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dev/telephone/{phoneIp}/servlet", (string phoneIp, HttpRequest request, ILoggerFactory loggers) =>
            {
                var key = request.Query["key"].ToString();
                loggers.CreateLogger("Annuaire.Dev.TelephoneSimule")
                    .LogInformation("☎ Téléphone simulé {PhoneIp} : commande « {Key} »", phoneIp, key);
                return Results.Text("OK");
            })
           .AllowAnonymous()
           .ExcludeFromDescription();
        return app;
    }
}
