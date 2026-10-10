namespace Annuaire.Web.Directory;

/// <summary>Source brute des entrées de l'annuaire (AD ou fichier simulé). Lecture seule.</summary>
public interface IDirectoryProvider
{
    Task<IReadOnlyList<DirectoryEntry>> LoadAsync(CancellationToken cancellationToken);
}

/// <summary>La source de données ne répond pas ou a renvoyé une erreur.</summary>
public sealed class DirectoryUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
