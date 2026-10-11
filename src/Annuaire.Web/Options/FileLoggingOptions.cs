namespace Annuaire.Web.Options;

/// <summary>Journaux sur disque (rotation quotidienne).</summary>
public sealed class FileLoggingOptions
{
    public const string SectionName = "FileLogging";

    /// <summary>Dossier des journaux (relatif au dossier de l'application ou absolu, ex. "D:\Logs\Annuaire").</summary>
    public string Directory { get; set; } = "logs";

    /// <summary>Journal des appels : nombre de jours conservés (données personnelles, 1 an par défaut).</summary>
    public int CallLogRetentionDays { get; set; } = 365;

    /// <summary>Journal technique de l'application : nombre de jours conservés.</summary>
    public int AppLogRetentionDays { get; set; } = 90;
}
