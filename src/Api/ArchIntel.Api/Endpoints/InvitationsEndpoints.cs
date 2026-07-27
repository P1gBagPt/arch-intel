using System.Security.Claims;
using ArchIntel.Api.Auth;
using ArchIntel.Api.Contracts;
using ArchIntel.Api.Problems;

namespace ArchIntel.Api.Endpoints;

/// <summary>`POST /repos/{repoId}/invitations` and the accept flow (05-rest-api.md Section 6.4).
/// Create/list are Owner-gated membership management; accept is deliberately NOT gated by
/// RequireRepoViewer/Owner — the whole point is granting access to someone who doesn't have it
/// yet, so it only requires *an* identity (or the fixed dev-mode placeholder when
/// Authentication:Enabled is false), not existing repo membership.</summary>
public static class InvitationsEndpoints
{
    /// <summary>Used as the acting identity when Authentication:Enabled is false — there's no real
    /// multi-user concept in unauthenticated/local-dev mode, so invitation-accept still needs some
    /// stable user id to record the resulting membership under.</summary>
    private const string LocalDevUserId = "local-dev-user";

    public static IEndpointRouteBuilder MapInvitationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/invitations", (string repoId, CreateInvitationRequest request, InvitationStore invitations) =>
        {
            if (!Enum.TryParse<RepoRole>(request.Role, ignoreCase: true, out var role))
            {
                return ProblemTypes.InvalidQuery($"Unknown role '{request.Role}'. Valid values: {string.Join(", ", Enum.GetNames<RepoRole>())}");
            }

            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return ProblemTypes.InvalidQuery("'email' is required.");
            }

            var invitation = invitations.Create(repoId, request.Email, role);
            return Results.Ok(new ApiEnvelope<InvitationDto>(ToDto(invitation)));
        })
        .WithName("PostInvitation")
        .WithTags("Auth")
        .RequireAuthorization("RequireRepoOwner")
        .ProducesValidationProblem();

        app.MapGet("/invitations", (string repoId, InvitationStore invitations) =>
            Results.Ok(new ApiEnvelope<IReadOnlyList<InvitationDto>>(invitations.ListForRepo(repoId).Select(ToDto).ToList())))
        .WithName("GetInvitations")
        .WithTags("Auth")
        .RequireAuthorization("RequireRepoOwner");

        app.MapPost("/invitations/{invitationId}/accept", (
            string repoId, string invitationId, HttpContext http, InvitationStore invitations, IRepoMembershipStore memberships, IConfiguration config) =>
        {
            var invitation = invitations.Get(invitationId);
            if (invitation is null || invitation.RepoId != repoId)
            {
                return ProblemTypes.InvitationNotFound(invitationId, $"/invitations/{invitationId}");
            }

            var authEnabled = config.GetValue("Authentication:Enabled", false);
            var userId = authEnabled ? http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value : LocalDevUserId;
            if (authEnabled && userId is null)
            {
                return Results.Problem(title: "Authentication required", statusCode: StatusCodes.Status401Unauthorized);
            }

            memberships.Upsert(new RepoMembership(userId!, repoId, invitation.Role));
            invitations.Update(invitation with { Status = InvitationStatus.Accepted });

            return Results.Ok(new ApiEnvelope<MembershipDto>(new MembershipDto(userId!, repoId, invitation.Role.ToString())));
        })
        .WithName("AcceptInvitation")
        .WithTags("Auth")
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static InvitationDto ToDto(Invitation invitation) => new(invitation.InvitationId, invitation.RepoId, invitation.Email, invitation.Role.ToString(), invitation.Status.ToString());
}
