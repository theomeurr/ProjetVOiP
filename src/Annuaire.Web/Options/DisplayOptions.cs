namespace Annuaire.Web.Options;

/// <summary>Ce qui est affiché dans l'annuaire.</summary>
public sealed class DisplayOptions
{
    public const string SectionName = "Display";

    /// <summary>Afficher le numéro de mobile sur les fiches.</summary>
    public bool ShowMobile { get; set; } = true;

    /// <summary>Si non vide, le mobile n'est affiché que pour ces services (valeurs de "department").</summary>
    public List<string> MobileDepartments { get; set; } = [];

    /// <summary>Titre de la section regroupant les objets contact.</summary>
    public string ContactsSectionTitle { get; set; } = "Accueil du public";

    public string OrganizationName { get; set; } = "Ville de Montmagny";
}
