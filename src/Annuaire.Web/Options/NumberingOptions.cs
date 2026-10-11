namespace Annuaire.Web.Options;

/// <summary>Règles de normalisation des numéros avant composition.</summary>
public sealed class NumberingOptions
{
    public const string SectionName = "Numbering";

    /// <summary>Nombre de chiffres d'un poste interne, composé tel quel.</summary>
    public int InternalExtensionLength { get; set; } = 4;

    /// <summary>Préfixe de prise de ligne extérieure (ex. "0"). Vide = aucun.</summary>
    public string OutsideLinePrefix { get; set; } = "0";

    /// <summary>
    /// Débuts de numéros nationaux correspondant aux SDA de la mairie (ex. "013428").
    /// Un tel numéro est converti en poste interne (ses derniers chiffres) au lieu de sortir sur le réseau public.
    /// </summary>
    public List<string> InternalSdaPrefixes { get; set; } = ["013428"];

    /// <summary>Indicatif du pays : +33 1 34… devient 01 34….</summary>
    public string CountryCode { get; set; } = "33";

    /// <summary>Préfixe international à composer pour les autres pays (+44… devient 0044…).</summary>
    public string InternationalPrefix { get; set; } = "00";
}
