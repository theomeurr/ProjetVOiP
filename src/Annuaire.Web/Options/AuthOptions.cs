namespace Annuaire.Web.Options;

/// <summary>Authentification. En production : Windows intégrée (Negotiate/Kerberos).</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Authentication";

    /// <summary>
    /// Compte simulé (sAMAccountName) pour développer sans domaine, ex. sur macOS.
    /// Ignoré hors de l'environnement Development.
    /// </summary>
    public string? DevelopmentUser { get; set; }
}
