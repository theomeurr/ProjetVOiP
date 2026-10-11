namespace Annuaire.Web.Calling;

public enum CallStatus
{
    Success,
    Disabled,
    NotAllowed,
    InvalidNumber,
    NotFound,
    PhoneUnreachable,
    ProviderError
}

/// <summary>Résultat d'une demande d'appel.</summary>
/// <param name="Message">Message destiné à l'agent (français, sans détail technique).</param>
/// <param name="ClientAction">URI que le navigateur doit ouvrir (tel:/sip:), pour le fournisseur côté client.</param>
/// <param name="Detail">Détail technique pour le journal uniquement, jamais renvoyé au navigateur.</param>
public sealed record CallResult(CallStatus Status, string? Message = null, string? ClientAction = null, string? Detail = null)
{
    public bool Success => Status == CallStatus.Success;

    public static CallResult Ok(string? clientAction = null, string? detail = null) =>
        new(CallStatus.Success, null, clientAction, detail);

    public static CallResult Fail(CallStatus status, string message, string? detail = null) =>
        new(status, message, null, detail);
}
