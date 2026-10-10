using System.Text.Json;
using System.Text.Json.Serialization;
using Annuaire.Web.Options;
using Microsoft.Extensions.Options;

namespace Annuaire.Web.Directory;

/// <summary>
/// Fournisseur simulé : lit un fichier JSON d'agents fictifs.
/// Permet de tout tester sans Active Directory (ex. sur macOS).
/// </summary>
public sealed class MockDirectoryProvider(
    IOptions<DirectoryOptions> options,
    IWebHostEnvironment environment,
    ILogger<MockDirectoryProvider> logger) : IDirectoryProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<DirectoryEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(environment.ContentRootPath, options.Value.MockFile);
        if (!File.Exists(path))
        {
            throw new DirectoryUnavailableException($"Fichier d'annuaire simulé introuvable : {path}");
        }

        await using var stream = File.OpenRead(path);
        var file = await JsonSerializer.DeserializeAsync<MockFile>(stream, JsonOptions, cancellationToken)
                   ?? throw new DirectoryUnavailableException("Fichier d'annuaire simulé vide ou invalide.");

        var baseDirectory = Path.GetDirectoryName(path)!;
        var entries = new List<DirectoryEntry>();
        foreach (var item in file.Entries.Where(e => e.Enabled))
        {
            entries.Add(new DirectoryEntry
            {
                Id = item.Id,
                Kind = item.Kind,
                AccountName = item.AccountName,
                DisplayName = item.DisplayName,
                GivenName = item.GivenName,
                Surname = item.Surname,
                Department = item.Department,
                Title = item.Title,
                TelephoneNumber = item.TelephoneNumber,
                IpPhone = item.IpPhone,
                Mobile = item.Mobile,
                Mail = item.Mail,
                Office = item.Office,
                PhoneIp = item.PhoneIp,
                Photo = await ReadPhotoAsync(baseDirectory, item.PhotoFile, cancellationToken)
            });
        }

        logger.LogInformation("Annuaire simulé chargé : {Count} entrées actives", entries.Count);
        return entries;
    }

    private static async Task<byte[]?> ReadPhotoAsync(string baseDirectory, string? photoFile, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(photoFile))
        {
            return null;
        }

        // Refuse tout chemin qui sortirait du dossier du fichier simulé.
        var root = Path.GetFullPath(baseDirectory) + Path.DirectorySeparatorChar;
        var photoPath = Path.GetFullPath(Path.Combine(root, photoFile));
        return photoPath.StartsWith(root, StringComparison.Ordinal) && File.Exists(photoPath)
            ? await File.ReadAllBytesAsync(photoPath, ct)
            : null;
    }

    private sealed class MockFile
    {
        public List<MockEntry> Entries { get; set; } = [];
    }

    private sealed class MockEntry
    {
        public string Id { get; set; } = "";
        public EntryKind Kind { get; set; } = EntryKind.Person;

        /// <summary>Simule userAccountControl : les comptes désactivés sont exclus.</summary>
        public bool Enabled { get; set; } = true;

        public string? AccountName { get; set; }
        public string DisplayName { get; set; } = "";
        public string? GivenName { get; set; }
        public string? Surname { get; set; }
        public string? Department { get; set; }
        public string? Title { get; set; }
        public string? TelephoneNumber { get; set; }
        public string? IpPhone { get; set; }
        public string? Mobile { get; set; }
        public string? Mail { get; set; }
        public string? Office { get; set; }
        public string? PhoneIp { get; set; }
        public string? PhotoFile { get; set; }
    }
}
