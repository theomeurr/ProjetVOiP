namespace Annuaire.Web.Options;

/// <summary>Choix de la source de données et paramètres du cache.</summary>
public sealed class DirectoryOptions
{
    public const string SectionName = "Directory";

    /// <summary>"Ldap" (production) ou "Mock" (développement uniquement).</summary>
    public DirectoryProviderKind Provider { get; set; } = DirectoryProviderKind.Ldap;

    /// <summary>Durée de vie du cache mémoire de l'annuaire, en minutes.</summary>
    public int CacheMinutes { get; set; } = 15;

    /// <summary>Fichier JSON d'agents fictifs (chemin relatif à la racine du contenu).</summary>
    public string MockFile { get; set; } = "Data/mock-directory.json";
}

public enum DirectoryProviderKind
{
    Ldap,
    Mock
}
