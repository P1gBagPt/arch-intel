using Microsoft.AspNetCore.SignalR;

namespace ArchIntel.Api.Realtime;

/// <summary>Phase 3 has no multi-repo/auth, so every event just goes to Clients.All — the implicit
/// single global group (05-rest-api.md Section 5.4). Per-repo group scoping is a Phase 4 addition.</summary>
public sealed class ArchitectureChangeNotifier(IHubContext<ArchitectureHub> hub) : IArchitectureChangeNotifier
{
    public Task ScanProgressAsync(ScanProgressEvent evt, CancellationToken ct = default)
        => hub.Clients.All.SendAsync("scan:progress", evt, ct);

    public Task GraphUpdatedAsync(GraphUpdatedEvent evt, CancellationToken ct = default)
        => hub.Clients.All.SendAsync("graph:updated", evt, ct);

    public Task MetricsUpdatedAsync(MetricsUpdatedEvent evt, CancellationToken ct = default)
        => hub.Clients.All.SendAsync("metrics:updated", evt, ct);

    public Task JobCompletedAsync(JobCompletedEvent evt, CancellationToken ct = default)
        => hub.Clients.All.SendAsync("job:completed", evt, ct);

    public Task JobFailedAsync(JobFailedEvent evt, CancellationToken ct = default)
        => hub.Clients.All.SendAsync("job:failed", evt, ct);
}
