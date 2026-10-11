using Annuaire.Web.Options;

namespace Annuaire.Web.Security;

/// <summary>
/// Vérifie la configuration au démarrage : l'application refuse de démarrer avec une configuration
/// incomplète ou dangereuse, avec un message clair dans le journal d'événements, plutôt que d'échouer plus tard.
/// </summary>
public static class ConfigurationValidator
{
    public static IReadOnlyList<string> Validate(
        IHostEnvironment environment,
        DirectoryOptions directory,
        LdapOptions ldap,
        CallingOptions calling,
        NumberingOptions numbering)
    {
        var errors = new List<string>();

        if (directory.Provider == DirectoryProviderKind.Mock && environment.IsProduction())
        {
            errors.Add("Le fournisseur d'annuaire simulé (Directory:Provider = Mock) est interdit en production.");
        }

        if (directory.CacheMinutes is < 1 or > 1440)
        {
            errors.Add("Directory:CacheMinutes doit être compris entre 1 et 1440.");
        }

        if (directory.Provider == DirectoryProviderKind.Ldap)
        {
            if (string.IsNullOrWhiteSpace(ldap.Server)) errors.Add("Ldap:Server est obligatoire (nom DNS du contrôleur de domaine).");
            if (string.IsNullOrWhiteSpace(ldap.BaseDn)) errors.Add("Ldap:BaseDn est obligatoire (ex. DC=montmagny,DC=local).");
            if (string.IsNullOrWhiteSpace(ldap.BindUser)) errors.Add("Ldap:BindUser est obligatoire (compte de service en lecture seule).");
            if (string.IsNullOrEmpty(ldap.BindPassword)) errors.Add("Ldap:BindPassword est obligatoire (à placer dans appsettings.Production.json ou une variable d'environnement).");
            if (!ldap.UseSsl && environment.IsProduction()) errors.Add("Ldap:UseSsl doit valoir true en production (LDAPS).");
        }

        switch (calling.Provider)
        {
            case CallProviderKind.YealinkActionUri:
                if (!calling.Yealink.UrlTemplate.Contains("{number}", StringComparison.OrdinalIgnoreCase))
                    errors.Add("Calling:Yealink:UrlTemplate doit contenir {number}.");
                if (!calling.Yealink.UrlTemplate.Contains("{callerIp}", StringComparison.OrdinalIgnoreCase) && environment.IsProduction())
                    errors.Add("Calling:Yealink:UrlTemplate doit contenir {callerIp} : la commande est envoyée au téléphone de l'appelant.");
                break;
            case CallProviderKind.GenericHttp:
                if (!Uri.TryCreate(calling.GenericHttp.UrlTemplate.Replace("{", "").Replace("}", ""), UriKind.Absolute, out _))
                    errors.Add("Calling:GenericHttp:UrlTemplate doit être une URL http(s) complète.");
                if (!calling.GenericHttp.UrlTemplate.Contains("{number}", StringComparison.OrdinalIgnoreCase)
                    && !calling.GenericHttp.BodyTemplate.Contains("{number}", StringComparison.OrdinalIgnoreCase))
                    errors.Add("Calling:GenericHttp doit utiliser {number} dans UrlTemplate ou BodyTemplate.");
                break;
            case CallProviderKind.TelLink:
                if (calling.TelLink.Scheme is not ("tel" or "sip" or "TEL" or "SIP"))
                    errors.Add("Calling:TelLink:Scheme doit valoir \"tel\" ou \"sip\".");
                break;
        }

        if (calling.TimeoutSeconds is < 1 or > 60) errors.Add("Calling:TimeoutSeconds doit être compris entre 1 et 60.");
        if (calling.MaxCallsPerMinute < 0) errors.Add("Calling:MaxCallsPerMinute doit être positif (0 = pas de limite).");

        foreach (var subnet in calling.AllowedPhoneSubnets.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            if (!System.Net.IPNetwork.TryParse(subnet.Trim(), out _))
                errors.Add($"Calling:AllowedPhoneSubnets contient une valeur invalide : « {subnet} » (format attendu : 10.20.0.0/16).");
        }

        if (numbering.InternalExtensionLength is < 2 or > 8) errors.Add("Numbering:InternalExtensionLength doit être compris entre 2 et 8.");
        if (!IsDigitsOrEmpty(numbering.OutsideLinePrefix)) errors.Add("Numbering:OutsideLinePrefix ne doit contenir que des chiffres.");
        if (!IsDigitsOrEmpty(numbering.CountryCode)) errors.Add("Numbering:CountryCode ne doit contenir que des chiffres.");
        if (!IsDigitsOrEmpty(numbering.InternationalPrefix)) errors.Add("Numbering:InternationalPrefix ne doit contenir que des chiffres.");

        return errors;
    }

    private static bool IsDigitsOrEmpty(string? value) => string.IsNullOrEmpty(value) || value.All(char.IsAsciiDigit);
}
