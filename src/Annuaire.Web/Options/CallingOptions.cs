namespace Annuaire.Web.Options;

/// <summary>Choix et paramètres du fournisseur d'appel (click-to-call).</summary>
public sealed class CallingOptions
{
    public const string SectionName = "Calling";

    public CallProviderKind Provider { get; set; } = CallProviderKind.Disabled;

    /// <summary>Délai maximal d'attente de la réponse du téléphone ou de l'IPBX.</summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Sous-réseaux (CIDR) où doivent se trouver les téléphones, ex. "10.20.0.0/16" (VLAN VoIP).
    /// Le serveur refuse d'envoyer une commande à une IP hors de ces réseaux. Vide = pas de restriction (déconseillé).
    /// </summary>
    public List<string> AllowedPhoneSubnets { get; set; } = [];

    /// <summary>Nombre maximal d'appels par minute et par agent. 0 = pas de limite (par défaut).</summary>
    public int MaxCallsPerMinute { get; set; }

    public YealinkOptions Yealink { get; set; } = new();

    public GenericHttpOptions GenericHttp { get; set; } = new();

    public TelLinkOptions TelLink { get; set; } = new();
}

public enum CallProviderKind
{
    Disabled,
    YealinkActionUri,
    GenericHttp,
    TelLink
}

/// <summary>Action URI des postes Yealink : le serveur demande au poste de l'appelant de composer.</summary>
public sealed class YealinkOptions
{
    /// <summary>Variables : {callerIp}, {callerExt}, {number}.</summary>
    public string UrlTemplate { get; set; } = "https://{callerIp}/servlet?key=number={number}";

    /// <summary>Compte web du téléphone (authentification Basic ou Digest selon le firmware).</summary>
    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    /// <summary>Les postes utilisent généralement un certificat auto-signé.</summary>
    public bool AcceptInvalidCertificate { get; set; } = true;
}

/// <summary>Appel générique d'une API HTTP d'IPBX (3CX, Wildix, Alcatel…), sans recoder.</summary>
public sealed class GenericHttpOptions
{
    /// <summary>GET, POST, PUT…</summary>
    public string Method { get; set; } = "GET";

    /// <summary>Variables : {callerExt}, {callerIp}, {number} (encodées pour une URL).</summary>
    public string UrlTemplate { get; set; } = "";

    /// <summary>Corps optionnel (POST/PUT). Les variables sont échappées selon ContentType (JSON ou formulaire).</summary>
    public string BodyTemplate { get; set; } = "";

    public string ContentType { get; set; } = "application/json";

    /// <summary>En-têtes supplémentaires, ex. "Authorization": "Bearer …". Les variables y sont aussi remplacées.</summary>
    public Dictionary<string, string> Headers { get; set; } = [];

    /// <summary>Authentification Basic optionnelle.</summary>
    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    public bool AcceptInvalidCertificate { get; set; }
}

/// <summary>Repli côté client : le navigateur ouvre un lien tel: ou sip: (softphone, Teams, téléphone mobile…).</summary>
public sealed class TelLinkOptions
{
    /// <summary>"tel" ou "sip".</summary>
    public string Scheme { get; set; } = "tel";

    /// <summary>Domaine SIP pour produire sip:numero@domaine (Scheme = "sip").</summary>
    public string SipDomain { get; set; } = "";
}
