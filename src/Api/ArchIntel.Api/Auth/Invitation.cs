namespace ArchIntel.Api.Auth;

public enum InvitationStatus { Pending, Accepted }

/// <summary>`POST /repos/{repoId}/invitations` (05-rest-api.md Section 6.4) — in-memory, same
/// restart-loses-state tradeoff as JobStore/InMemoryRepoMembershipStore. There's no real email
/// service in this repo, so "inviting" an email doesn't send anything; it just records who's
/// allowed to accept.</summary>
public sealed record Invitation
{
    public required string InvitationId { get; init; }
    public required string RepoId { get; init; }
    public required string Email { get; init; }
    public required RepoRole Role { get; init; }
    public InvitationStatus Status { get; init; } = InvitationStatus.Pending;
}
