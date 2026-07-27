namespace ArchIntel.Api.Auth;

/// <summary>In-memory `RepoMembership` table (05-rest-api.md Section 6.3) — no real user/account
/// database exists in this repo, so this mirrors JobStore's pattern (singleton, in-memory,
/// restart loses state). Seeded at startup from `RepoMemberships` config (see appsettings.json)
/// so a real deployment has at least one bootstrap Owner instead of a permanent chicken-and-egg
/// problem, and mutated at runtime via the invitation accept flow.</summary>
public interface IRepoMembershipStore
{
    RepoRole? GetRole(string userId, string repoId);

    void Upsert(RepoMembership membership);
}
