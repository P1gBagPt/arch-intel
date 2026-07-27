using System.Security.Claims;
using ArchIntel.Api.Auth;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;

namespace ArchIntel.Api.Realtime;

/// <summary>`/hubs/architecture` (05-rest-api.md Section 5.1). Phase 3 (no auth): every client is
/// an implicit member of one global group — JoinRepo/LeaveRepo are unneeded and unused. Phase 4
/// (Authentication:Enabled = true): clients must call JoinRepo(repoId) after connecting; the hub
/// validates the connection's authenticated ClaimsPrincipal against RepoMembership before adding
/// them to the group (Section 5.4) — the same RepoRole check the HTTP endpoints use, just without
/// the ASP.NET Core authorization-policy machinery (SignalR hub methods don't go through
/// IAuthorizationHandler the way minimal API endpoints do).</summary>
public sealed class ArchitectureHub(IRepoMembershipStore membershipStore, IConfiguration configuration) : Hub
{
    public static string GroupName(string repoId) => $"repo:{repoId}";

    public async Task JoinRepo(string repoId)
    {
        if (!IsAuthorized(repoId))
        {
            throw new HubException($"Not authorized to join repo '{repoId}'.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(repoId));
    }

    public async Task LeaveRepo(string repoId) => await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(repoId));

    private bool IsAuthorized(string repoId)
    {
        if (!configuration.GetValue("Authentication:Enabled", false))
        {
            return true;
        }

        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return userId is not null && membershipStore.GetRole(userId, repoId) is not null;
    }
}
