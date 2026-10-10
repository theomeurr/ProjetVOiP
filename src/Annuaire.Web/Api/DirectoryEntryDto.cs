using Annuaire.Web.Directory;
using Annuaire.Web.Options;

namespace Annuaire.Web.Api;

/// <summary>
/// Fiche envoyée au navigateur. Ne contient ni le compte Windows, ni l'IP du téléphone, ni la photo brute.
/// </summary>
public sealed record DirectoryEntryDto(
    string Id,
    EntryKind Kind,
    string DisplayName,
    string? GivenName,
    string? Surname,
    string? Department,
    string? Title,
    string? TelephoneNumber,
    string? IpPhone,
    string? Mobile,
    string? Mail,
    string? Office,
    bool HasPhoto,
    string Search)
{
    public static DirectoryEntryDto From(DirectoryEntry entry, DisplayOptions display)
    {
        var mobile = IsMobileVisible(entry, display) ? entry.Mobile : null;
        return new DirectoryEntryDto(
            entry.Id,
            entry.Kind,
            entry.DisplayName,
            entry.GivenName,
            entry.Surname,
            entry.Department,
            entry.Title,
            entry.TelephoneNumber,
            entry.IpPhone,
            mobile,
            entry.Mail,
            entry.Office,
            entry.HasPhoto,
            // Texte pré-normalisé (minuscules, sans accents) pour une recherche instantanée côté client.
            TextNormalizer.ForSearch(
                entry.DisplayName, entry.GivenName, entry.Surname, entry.Department, entry.Title,
                entry.Office, entry.IpPhone, Digits(entry.TelephoneNumber), Digits(mobile)));
    }

    internal static bool IsMobileVisible(DirectoryEntry entry, DisplayOptions display)
    {
        if (!display.ShowMobile || string.IsNullOrWhiteSpace(entry.Mobile))
        {
            return false;
        }

        return display.MobileDepartments.Count == 0
               || (entry.Department is not null
                   && display.MobileDepartments.Contains(entry.Department, StringComparer.OrdinalIgnoreCase));
    }

    private static string? Digits(string? number) =>
        number is null ? null : new string(number.Where(char.IsAsciiDigit).ToArray());
}

public sealed record DirectoryResponse(
    string Organization,
    string ContactsTitle,
    DateTimeOffset LoadedAt,
    IReadOnlyList<string> Departments,
    IReadOnlyList<DirectoryEntryDto> Entries);
