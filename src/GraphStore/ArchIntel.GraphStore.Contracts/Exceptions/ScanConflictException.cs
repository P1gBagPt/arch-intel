namespace ArchIntel.GraphStore.Contracts.Exceptions;

/// <summary>Thrown when a caller tries to begin a scan for a <c>repo_id</c> that already has a running scan.</summary>
public sealed class ScanConflictException : Exception
{
    public string RepoId { get; }

    public ScanConflictException(string repoId)
        : base($"A scan is already in progress for repo '{repoId}'.")
    {
        RepoId = repoId;
    }
}
