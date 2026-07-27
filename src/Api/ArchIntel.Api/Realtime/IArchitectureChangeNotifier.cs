namespace ArchIntel.Api.Realtime;

/// <summary>Relays watcher/job events to connected dashboard clients over ArchitectureHub
/// (05-rest-api.md Section 5.2). An interface (rather than calling IHubContext directly from the
/// Incremental Watcher's integration point or from PlanningEndpoints) so tests can trigger these
/// events without a real watcher process or a real SignalR connection driving them — see
/// ArchIntel.Api.Tests' SignalR integration test, which injects a fake event through this exact
/// seam per 05-rest-api.md Section 9.1.
///
/// `repoId` targets the Phase 4 `repo:{repoId}` group (Section 5.4) when `Authentication:Enabled`
/// is true; when it's false (default), events still broadcast to every connected client — the
/// Phase 3 behavior — since there's no JoinRepo call to have scoped anyone into a group yet.</summary>
public interface IArchitectureChangeNotifier
{
    Task ScanProgressAsync(string repoId, ScanProgressEvent evt, CancellationToken ct = default);

    Task GraphUpdatedAsync(string repoId, GraphUpdatedEvent evt, CancellationToken ct = default);

    Task MetricsUpdatedAsync(string repoId, MetricsUpdatedEvent evt, CancellationToken ct = default);

    Task JobCompletedAsync(string repoId, JobCompletedEvent evt, CancellationToken ct = default);

    Task JobFailedAsync(string repoId, JobFailedEvent evt, CancellationToken ct = default);
}
