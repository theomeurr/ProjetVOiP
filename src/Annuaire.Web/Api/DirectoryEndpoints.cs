using Annuaire.Web.Directory;
using Annuaire.Web.Options;
using Microsoft.Extensions.Options;

namespace Annuaire.Web.Api;

public static class DirectoryEndpoints
{
    public const string UnavailableMessage =
        "L'annuaire est momentanément indisponible. Réessayez dans quelques instants ; si le problème persiste, contactez la DSI.";

    public static IEndpointRouteBuilder MapDirectoryEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/directory", GetDirectoryAsync);
        api.MapGet("/photo/{id}", GetPhotoAsync);

        return app;
    }

    private static async Task<IResult> GetDirectoryAsync(
        DirectoryService directory, IOptions<DisplayOptions> display, HttpContext http, CancellationToken ct)
    {
        try
        {
            var snapshot = await directory.GetSnapshotAsync(ct);
            var options = display.Value;
            var response = new DirectoryResponse(
                options.OrganizationName,
                options.ContactsSectionTitle,
                snapshot.LoadedAt,
                snapshot.Departments,
                snapshot.Entries.Select(e => DirectoryEntryDto.From(e, options)).ToList());

            http.Response.Headers.CacheControl = "private, no-cache";
            return Results.Ok(response);
        }
        catch (DirectoryUnavailableException)
        {
            return Results.Json(new { message = UnavailableMessage }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> GetPhotoAsync(string id, DirectoryService directory, HttpContext http, CancellationToken ct)
    {
        try
        {
            var entry = (await directory.GetSnapshotAsync(ct)).FindById(id);
            if (entry?.Photo is not { Length: > 0 } photo)
            {
                return Results.NotFound();
            }

            http.Response.Headers.CacheControl = "private, max-age=3600";
            return Results.File(photo, DetectImageType(photo));
        }
        catch (DirectoryUnavailableException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>thumbnailPhoto est généralement un JPEG, parfois un PNG.</summary>
    internal static string DetectImageType(ReadOnlySpan<byte> data) =>
        data is [0x89, 0x50, 0x4E, 0x47, ..] ? "image/png" : "image/jpeg";
}
