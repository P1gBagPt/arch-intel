using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;

namespace ArchIntel.Api.Auth;

public sealed class RepoAuthorizationRequirement(RepoRole minimumRole) : IAuthorizationRequirement
{
    public RepoRole MinimumRole { get; } = minimumRole;
}

/// <summary>Checks the route's `repoId` against RepoMembership for the authenticated user
/// (05-rest-api.md Section 6.3's `IAuthorizationHandler`). When `Authentication:Enabled` is false
/// (the default — matches Phase 1-3's "local dev tool, no auth" behavior and Section 6.1's
/// `Authentication:Enabled` feature flag), every check succeeds unconditionally.
///
/// Important limitation: this enforces real, working ACCESS CONTROL on the repoId label in the
/// route, but does NOT partition the underlying graph data — the real IGraphReader is bound to
/// one SQLite database per process (single-repo), so a Viewer of repoId "a" and a Viewer of
/// repoId "b" currently see the same underlying graph data if both labels happen to be
/// authorized. True per-repo data partitioning depends on 02-graph-store.md's own (unbuilt)
/// multi-repo storage model and is out of scope here.</summary>
public sealed class RepoAuthorizationHandler(IRepoMembershipStore membershipStore, IConfiguration configuration)
    : AuthorizationHandler<RepoAuthorizationRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RepoAuthorizationRequirement requirement)
    {
        if (!configuration.GetValue("Authentication:Enabled", false))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (context.Resource is not HttpContext httpContext)
        {
            return Task.CompletedTask;
        }

        var repoId = httpContext.Request.RouteValues["repoId"] as string;
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (repoId is null || userId is null)
        {
            return Task.CompletedTask;
        }

        var role = membershipStore.GetRole(userId, repoId);
        if (role is not null && role >= requirement.MinimumRole)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
