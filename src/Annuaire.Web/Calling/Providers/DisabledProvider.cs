namespace Annuaire.Web.Calling.Providers;

/// <summary>Click-to-call désactivé : l'annuaire reste consultable, les numéros restent copiables.</summary>
public sealed class DisabledProvider : ICallProvider
{
    public const string UserMessage = "L'appel en un clic n'est pas activé sur cet annuaire. Vous pouvez copier les numéros.";

    public string Name => "Disabled";

    public bool IsEnabled => false;

    public bool RequiresPhoneIp => false;

    public Task<CallResult> DialAsync(CallerInfo caller, string number) =>
        Task.FromResult(CallResult.Fail(CallStatus.Disabled, UserMessage));
}
