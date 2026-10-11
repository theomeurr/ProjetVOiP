using System.Text.Json.Serialization;
using Annuaire.Web.Calling;
using Microsoft.AspNetCore.Antiforgery;

namespace Annuaire.Web.Api;

public static class CallEndpoints
{
    public static IEndpointRouteBuilder MapCallEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/call", DialAsync)
           .RequireAuthorization()
           // La validation anti-CSRF est faite explicitement ci-dessous (corps JSON + jeton dans l'en-tête).
           .DisableAntiforgery();
        return app;
    }

    private static async Task<IResult> DialAsync(HttpContext http, IAntiforgery antiforgery, CallService calls, CancellationToken ct)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Json(
                new CallResponse(false, "Votre session a expiré. Rechargez la page puis réessayez."),
                statusCode: StatusCodes.Status400BadRequest);
        }

        CallRequest? request;
        try
        {
            request = await http.Request.ReadFromJsonAsync<CallRequest>(ct);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or BadHttpRequestException)
        {
            request = null;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.EntryId) || request.Field is null)
        {
            return Results.Json(new CallResponse(false, "Demande d'appel invalide. Rechargez la page."), statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await calls.DialAsync(http.User, request.EntryId, request.Field.Value, ct);
        var statusCode = result.Status switch
        {
            CallStatus.Success => StatusCodes.Status200OK,
            CallStatus.Disabled or CallStatus.NotAllowed => StatusCodes.Status403Forbidden,
            CallStatus.NotFound => StatusCodes.Status404NotFound,
            CallStatus.InvalidNumber => StatusCodes.Status422UnprocessableEntity,
            CallStatus.PhoneUnreachable => StatusCodes.Status504GatewayTimeout,
            _ => StatusCodes.Status502BadGateway
        };

        // Detail (technique) n'est jamais renvoyé au navigateur.
        return Results.Json(new CallResponse(result.Success, result.Message, result.ClientAction), statusCode: statusCode);
    }

    private sealed record CallRequest(string? EntryId, CallField? Field);

    private sealed record CallResponse(
        bool Success,
        string? Message,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ClientAction = null);
}
