namespace ArchIntel.Api.Contracts;

public sealed record CreateInvitationRequest(string Email, string Role);

public sealed record InvitationDto(string InvitationId, string RepoId, string Email, string Role, string Status);

public sealed record MembershipDto(string UserId, string RepoId, string Role);
