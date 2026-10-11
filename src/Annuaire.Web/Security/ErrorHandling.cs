using Microsoft.AspNetCore.Diagnostics;

namespace Annuaire.Web.Security;

/// <summary>
/// Gestion des erreurs non prévues : jamais de trace technique à l'écran.
/// API → JSON avec un message clair ; pages → page d'erreur.
/// </summary>
public static class ErrorHandling
{
    public static IApplicationBuilder UseFriendlyErrors(this IApplicationBuilder app)
    {
        return app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
        {
            var feature = context.Features.Get<IExceptionHandlerPathFeature>();
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Annuaire.Errors");
            logger.LogError(feature?.Error, "Erreur non gérée sur {Path} (référence {TraceId})", feature?.Path, context.TraceIdentifier);

            if (feature?.Path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) == true)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    message = $"Une erreur interne est survenue. Réessayez ; si le problème persiste, contactez la DSI (référence {context.TraceIdentifier})."
                });
                return;
            }

            context.Response.Redirect($"{context.Request.PathBase}/Error?ref={Uri.EscapeDataString(context.TraceIdentifier)}");
        }));
    }

    /// <summary>Routes inconnues : 404 JSON pour l'API, page « introuvable » sinon.</summary>
    public static void MapNotFoundFallbacks(this IEndpointRouteBuilder app)
    {
        app.MapFallback("/api/{**path}", () =>
            Results.Json(new { success = false, message = "Ressource introuvable." }, statusCode: StatusCodes.Status404NotFound));
        app.MapFallbackToPage("/NotFound");
    }
}
