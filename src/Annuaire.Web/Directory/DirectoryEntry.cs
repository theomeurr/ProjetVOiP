namespace Annuaire.Web.Directory;

public enum EntryKind
{
    /// <summary>Agent (objet utilisateur AD).</summary>
    Person,

    /// <summary>Entrée non nominative (objet contact AD) : Accueil, CNI/Passeport…</summary>
    Contact
}

/// <summary>
/// Entrée de l'annuaire telle que lue dans l'AD. Contient des données internes
/// (compte, IP du téléphone, photo) qui ne doivent jamais être envoyées telles quelles au navigateur :
/// voir <see cref="Annuaire.Web.Api.DirectoryEntryDto"/>.
/// </summary>
public sealed record DirectoryEntry
{
    /// <summary>Identifiant stable (objectGUID en production).</summary>
    public required string Id { get; init; }

    public EntryKind Kind { get; init; } = EntryKind.Person;

    /// <summary>sAMAccountName, sert à reconnaître l'utilisateur Windows connecté.</summary>
    public string? AccountName { get; init; }

    public required string DisplayName { get; init; }
    public string? GivenName { get; init; }
    public string? Surname { get; init; }
    public string? Department { get; init; }
    public string? Title { get; init; }

    /// <summary>telephoneNumber : numéro direct (SDA).</summary>
    public string? TelephoneNumber { get; init; }

    /// <summary>ipPhone : poste interne.</summary>
    public string? IpPhone { get; init; }

    public string? Mobile { get; init; }
    public string? Mail { get; init; }

    /// <summary>physicalDeliveryOfficeName : bureau / site.</summary>
    public string? Office { get; init; }

    /// <summary>Adresse IP du téléphone physique (attribut configurable). Jamais exposée au client.</summary>
    public string? PhoneIp { get; init; }

    /// <summary>thumbnailPhoto (JPEG). Servie séparément par /api/photo/{id}.</summary>
    public byte[]? Photo { get; init; }

    public bool HasPhoto => Photo is { Length: > 0 };
}
