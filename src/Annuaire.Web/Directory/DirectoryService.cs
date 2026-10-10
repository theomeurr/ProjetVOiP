using Annuaire.Web.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Annuaire.Web.Directory;

/// <summary>
/// Accès à l'annuaire avec cache mémoire. Si la source devient indisponible,
/// la dernière version connue continue d'être servie et un nouvel essai a lieu une minute plus tard.
/// </summary>
public sealed class DirectoryService(
    IDirectoryProvider provider,
    IMemoryCache cache,
    IOptions<DirectoryOptions> options,
    TimeProvider timeProvider,
    ILogger<DirectoryService> logger)
{
    private const string CacheKey = "directory:snapshot";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private DirectorySnapshot? _lastGood;

    public async Task<DirectorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out DirectorySnapshot? cached) && cached is not null)
        {
            return cached;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Une autre requête a pu recharger pendant l'attente du verrou.
            if (cache.TryGetValue(CacheKey, out cached) && cached is not null)
            {
                return cached;
            }

            try
            {
                var entries = await provider.LoadAsync(cancellationToken);
                var snapshot = DirectorySnapshot.Create(entries, timeProvider.GetUtcNow());
                _lastGood = snapshot;
                cache.Set(CacheKey, snapshot, TimeSpan.FromMinutes(Math.Max(1, options.Value.CacheMinutes)));
                return snapshot;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (_lastGood is not null)
                {
                    logger.LogWarning(ex, "Rechargement de l'annuaire impossible, la version du {LoadedAt:u} reste servie", _lastGood.LoadedAt);
                    cache.Set(CacheKey, _lastGood, RetryDelay);
                    return _lastGood;
                }

                logger.LogError(ex, "Chargement initial de l'annuaire impossible");
                throw ex as DirectoryUnavailableException
                      ?? new DirectoryUnavailableException("Impossible de charger l'annuaire.", ex);
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Retrouve la fiche de l'utilisateur Windows connecté.</summary>
    public async Task<DirectoryEntry?> FindCurrentUserAsync(string? identityName, CancellationToken cancellationToken = default)
    {
        var account = AccountName.FromIdentity(identityName);
        if (account is null)
        {
            return null;
        }

        var snapshot = await GetSnapshotAsync(cancellationToken);
        return snapshot.FindByAccount(account);
    }
}
