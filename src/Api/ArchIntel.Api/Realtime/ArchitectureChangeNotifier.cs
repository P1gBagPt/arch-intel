using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;

namespace ArchIntel.Api.Realtime;

/// <summary>Phase 3 (Authentication:Enabled = false, the default): every client is an implicit
/// member of one global group, so events go to Clients.All. Phase 4 (Authentication:Enabled =
/// true): clients must call JoinRepo(repoId) first (see ArchitectureHub), so events target only
/// that repo's group (05-rest-api.md Section 5.4).</summary>
public sealed class ArchitectureChangeNotifier(IHubContext<ArchitectureHub> hub, IConfiguration configuration) : IArchitectureChangeNotifier
{
    public Task ScanProgressAsync(string repoId, ScanProgressEvent evt, CancellationToken ct = default)
        => Clients(repoId).SendAsync("scan:progress", evt, ct);

    public Task GraphUpdatedAsync(string repoId, GraphUpdatedEvent evt, CancellationToken ct = default)
        => Clients(repoId).SendAsync("graph:updated", evt, ct);

    public Task MetricsUpdatedAsync(string repoId, MetricsUpdatedEvent evt, CancellationToken ct = default)
        => Clients(repoId).SendAsync("metrics:updated", evt, ct);

    public Task JobCompletedAsync(string repoId, JobCompletedEvent evt, CancellationToken ct = default)
        => Clients(repoId).SendAsync("job:completed", evt, ct);

    public Task JobFailedAsync(string repoId, JobFailedEvent evt, CancellationToken ct = default)
        => Clients(repoId).SendAsync("job:failed", evt, ct);

    private IClientProxy Clients(string repoId) => configuration.GetValue("Authentication:Enabled", false)
        ? hub.Clients.Group(ArchitectureHub.GroupName(repoId))
        : hub.Clients.All;
}
