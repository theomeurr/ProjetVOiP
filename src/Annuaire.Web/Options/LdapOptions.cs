namespace Annuaire.Web.Options;

/// <summary>Connexion LDAPS à l'Active Directory (lecture seule).</summary>
public sealed class LdapOptions
{
    public const string SectionName = "Ldap";

    /// <summary>Nom DNS du contrôleur de domaine (doit correspondre au certificat LDAPS).</summary>
    public string Server { get; set; } = "";

    public int Port { get; set; } = 636;

    /// <summary>LDAPS. Ne désactiver qu'en test, jamais en production.</summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>Compte de service en lecture seule, ex. "svc-annuaire@montmagny.local".</summary>
    public string BindUser { get; set; } = "";

    public string BindPassword { get; set; } = "";

    /// <summary>Racine de recherche, ex. "DC=montmagny,DC=local".</summary>
    public string BaseDn { get; set; } = "";

    /// <summary>OU de recherche des utilisateurs. Vide = BaseDn.</summary>
    public List<string> IncludeOus { get; set; } = [];

    /// <summary>OU à exclure (et leurs sous-OU), ex. comptes techniques, élus non affichés.</summary>
    public List<string> ExcludeOus { get; set; } = [];

    /// <summary>Filtre des utilisateurs. Par défaut : personnes, comptes non désactivés.</summary>
    public string UserFilter { get; set; } =
        "(&(objectCategory=person)(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))";

    /// <summary>OU des objets contact non nominatifs (Accueil, CNI/Passeport…). Vide = pas de contacts.</summary>
    public string ContactsOu { get; set; } = "";

    public string ContactFilter { get; set; } = "(objectClass=contact)";

    /// <summary>Attribut contenant l'adresse IP du téléphone de l'agent.</summary>
    public string PhoneIpAttribute { get; set; } = "extensionAttribute1";

    /// <summary>Lire thumbnailPhoto (augmente la taille du cache).</summary>
    public bool LoadPhotos { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 15;

    public int PageSize { get; set; } = 500;
}
