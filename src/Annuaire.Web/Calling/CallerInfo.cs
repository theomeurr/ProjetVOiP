namespace Annuaire.Web.Calling;

/// <summary>
/// Appelant, construit uniquement côté serveur à partir de l'utilisateur Windows authentifié et de sa fiche AD.
/// Aucune de ces valeurs ne provient du navigateur.
/// </summary>
public sealed record CallerInfo(string AccountName, string DisplayName, string? Extension, string? PhoneIp);
