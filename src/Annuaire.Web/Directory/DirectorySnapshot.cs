using System.Globalization;

namespace Annuaire.Web.Directory;

/// <summary>Photographie de l'annuaire à un instant donné, indexée pour les recherches serveur.</summary>
public sealed class DirectorySnapshot
{
    private static readonly StringComparer FrenchComparer =
        StringComparer.Create(CultureInfo.GetCultureInfo("fr-FR"), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

    private readonly Dictionary<string, DirectoryEntry> _byId;
    private readonly Dictionary<string, DirectoryEntry> _byAccount;

    private DirectorySnapshot(IReadOnlyList<DirectoryEntry> entries, DateTimeOffset loadedAt)
    {
        Entries = entries;
        LoadedAt = loadedAt;
        _byId = entries.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
        _byAccount = entries
            .Where(e => e is { Kind: EntryKind.Person, AccountName: not null })
            .GroupBy(e => e.AccountName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        Departments = entries
            .Select(e => e.Department)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d!)
            .Distinct(FrenchComparer)
            .Order(FrenchComparer)
            .ToList();
    }

    /// <summary>Entrées triées : contacts d'abord, puis agents par nom puis prénom.</summary>
    public IReadOnlyList<DirectoryEntry> Entries { get; }

    public IReadOnlyList<string> Departments { get; }

    public DateTimeOffset LoadedAt { get; }

    public static DirectorySnapshot Create(IEnumerable<DirectoryEntry> entries, DateTimeOffset loadedAt)
    {
        var sorted = entries
            .DistinctBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e.Kind == EntryKind.Contact ? 0 : 1)
            .ThenBy(e => e.Surname ?? e.DisplayName, FrenchComparer)
            .ThenBy(e => e.GivenName ?? "", FrenchComparer)
            .ToList();
        return new DirectorySnapshot(sorted, loadedAt);
    }

    public DirectoryEntry? FindById(string id) => _byId.GetValueOrDefault(id);

    public DirectoryEntry? FindByAccount(string accountName) => _byAccount.GetValueOrDefault(accountName);
}
