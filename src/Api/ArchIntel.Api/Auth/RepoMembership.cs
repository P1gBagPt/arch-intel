namespace ArchIntel.Api.Auth;

/// <summary>`(userId, repoId, role)` (05-rest-api.md Section 6.3) — API-owned, not a Graph Store
/// concern (the Graph Store has no concept of users).</summary>
public sealed record RepoMembership(string UserId, string RepoId, RepoRole Role);
