using System.Collections.Concurrent;

namespace ArchIntel.Api.Auth;

public sealed class InMemoryRepoMembershipStore : IRepoMembershipStore
{
    private readonly ConcurrentDictionary<(string UserId, string RepoId), RepoRole> _memberships = new();

    public RepoRole? GetRole(string userId, string repoId)
        => _memberships.TryGetValue((userId, repoId), out var role) ? role : null;

    public void Upsert(RepoMembership membership) => _memberships[(membership.UserId, membership.RepoId)] = membership.Role;
}
