using System.DirectoryServices.Protocols;
using System.Net;
using Annuaire.Web.Options;
using Microsoft.Extensions.Options;

namespace Annuaire.Web.Directory;

/// <summary>
/// Lecture de l'Active Directory via LDAPS (System.DirectoryServices.Protocols, multiplateforme).
/// Aucune opération d'écriture n'est effectuée.
/// </summary>
public sealed class LdapDirectoryProvider(
    IOptions<LdapOptions> options,
    ILogger<LdapDirectoryProvider> logger) : IDirectoryProvider
{
    private const int AccountDisableFlag = 0x2;

    public Task<IReadOnlyList<DirectoryEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        // L'API LDAP est synchrone : on la déporte sur le pool de threads.
        return Task.Run(() => Load(cancellationToken), cancellationToken);
    }

    private IReadOnlyList<DirectoryEntry> Load(CancellationToken cancellationToken)
    {
        var config = options.Value;
        if (string.IsNullOrWhiteSpace(config.Server) || string.IsNullOrWhiteSpace(config.BaseDn))
        {
            throw new DirectoryUnavailableException("Configuration LDAP incomplète (Ldap:Server et Ldap:BaseDn).");
        }

        try
        {
            using var connection = Connect(config);
            var attributes = BuildAttributeList(config);
            var entries = new List<DirectoryEntry>();

            var userBases = config.IncludeOus.Count > 0 ? config.IncludeOus : [config.BaseDn];
            foreach (var searchBase in userBases)
            {
                foreach (var result in PagedSearch(connection, searchBase, config.UserFilter, attributes, config, cancellationToken))
                {
                    if (DnFilter.IsExcluded(result.DistinguishedName, config.ExcludeOus) || IsDisabled(result))
                    {
                        continue;
                    }

                    if (Map(result, EntryKind.Person, config) is { } entry)
                    {
                        entries.Add(entry);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(config.ContactsOu))
            {
                foreach (var result in PagedSearch(connection, config.ContactsOu, config.ContactFilter, attributes, config, cancellationToken))
                {
                    if (Map(result, EntryKind.Contact, config) is { } entry)
                    {
                        entries.Add(entry);
                    }
                }
            }

            // Un même objet peut remonter par deux OU incluses qui se chevauchent.
            var distinct = entries.DistinctBy(e => e.Id).ToList();
            logger.LogInformation("Annuaire AD chargé : {Count} entrées", distinct.Count);
            return distinct;
        }
        catch (Exception ex) when (ex is LdapException or DirectoryOperationException or DirectoryException)
        {
            throw new DirectoryUnavailableException("Impossible de lire l'Active Directory.", ex);
        }
    }

    private static LdapConnection Connect(LdapOptions config)
    {
        var identifier = new LdapDirectoryIdentifier(config.Server, config.Port, fullyQualifiedDnsHostName: true, connectionless: false);
        var connection = new LdapConnection(identifier)
        {
            AuthType = AuthType.Basic,
            Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds),
            Credential = new NetworkCredential(config.BindUser, config.BindPassword)
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.SecureSocketLayer = config.UseSsl;
        // Pas de suivi des referrals : on reste sur le domaine configuré.
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        connection.Bind();
        return connection;
    }

    private static string[] BuildAttributeList(LdapOptions config)
    {
        var attributes = new List<string>
        {
            "objectGUID", "sAMAccountName", "userAccountControl",
            "displayName", "givenName", "sn", "cn", "department", "title",
            "telephoneNumber", "ipPhone", "mobile", "mail", "physicalDeliveryOfficeName"
        };
        if (config.LoadPhotos)
        {
            attributes.Add("thumbnailPhoto");
        }

        if (!string.IsNullOrWhiteSpace(config.PhoneIpAttribute))
        {
            attributes.Add(config.PhoneIpAttribute);
        }

        return attributes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<SearchResultEntry> PagedSearch(
        LdapConnection connection, string searchBase, string filter, string[] attributes,
        LdapOptions config, CancellationToken cancellationToken)
    {
        var pageControl = new PageResultRequestControl(config.PageSize);
        var request = new SearchRequest(searchBase, filter, SearchScope.Subtree, attributes);
        request.Controls.Add(pageControl);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = (SearchResponse)connection.SendRequest(request, TimeSpan.FromSeconds(config.TimeoutSeconds));
            foreach (SearchResultEntry entry in response.Entries)
            {
                yield return entry;
            }

            var pageResponse = response.Controls.OfType<PageResultResponseControl>().FirstOrDefault();
            if (pageResponse is null || pageResponse.Cookie.Length == 0)
            {
                yield break;
            }

            pageControl.Cookie = pageResponse.Cookie;
        }
    }

    private static bool IsDisabled(SearchResultEntry result)
    {
        // Double contrôle : le filtre par défaut exclut déjà les comptes désactivés,
        // mais un filtre personnalisé pourrait l'oublier.
        var value = GetString(result, "userAccountControl");
        return int.TryParse(value, out var flags) && (flags & AccountDisableFlag) != 0;
    }

    private static DirectoryEntry? Map(SearchResultEntry result, EntryKind kind, LdapOptions config)
    {
        var guidBytes = GetBytes(result, "objectGUID");
        var id = guidBytes is { Length: 16 } ? new Guid(guidBytes).ToString("N") : null;
        var displayName = GetString(result, "displayName") ?? GetString(result, "cn");
        if (id is null || string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        return new DirectoryEntry
        {
            Id = id,
            Kind = kind,
            AccountName = kind == EntryKind.Person ? GetString(result, "sAMAccountName") : null,
            DisplayName = displayName,
            GivenName = GetString(result, "givenName"),
            Surname = GetString(result, "sn"),
            Department = GetString(result, "department"),
            Title = GetString(result, "title"),
            TelephoneNumber = GetString(result, "telephoneNumber"),
            IpPhone = GetString(result, "ipPhone"),
            Mobile = GetString(result, "mobile"),
            Mail = GetString(result, "mail"),
            Office = GetString(result, "physicalDeliveryOfficeName"),
            PhoneIp = string.IsNullOrWhiteSpace(config.PhoneIpAttribute) ? null : GetString(result, config.PhoneIpAttribute),
            Photo = config.LoadPhotos ? GetBytes(result, "thumbnailPhoto") : null
        };
    }

    private static string? GetString(SearchResultEntry result, string attribute)
    {
        if (!result.Attributes.Contains(attribute))
        {
            return null;
        }

        var values = result.Attributes[attribute].GetValues(typeof(string));
        return values.Length > 0 && values[0] is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
    }

    private static byte[]? GetBytes(SearchResultEntry result, string attribute)
    {
        if (!result.Attributes.Contains(attribute))
        {
            return null;
        }

        var values = result.Attributes[attribute].GetValues(typeof(byte[]));
        return values.Length > 0 ? values[0] as byte[] : null;
    }
}

public static class DnFilter
{
    /// <summary>
    /// Vrai si le DN appartient à l'une des OU exclues (ou à l'une de leurs sous-OU).
    /// La comparaison ignore la casse et les espaces après les virgules.
    /// </summary>
    public static bool IsExcluded(string distinguishedName, IEnumerable<string> excludedOus)
    {
        var dn = Canonicalize(distinguishedName);
        foreach (var ou in excludedOus)
        {
            if (string.IsNullOrWhiteSpace(ou))
            {
                continue;
            }

            var suffix = Canonicalize(ou);
            if (dn.Equals(suffix, StringComparison.OrdinalIgnoreCase)
                || dn.EndsWith("," + suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string Canonicalize(string dn) =>
        string.Join(',', dn.Split(',').Select(part => part.Trim()));
}
