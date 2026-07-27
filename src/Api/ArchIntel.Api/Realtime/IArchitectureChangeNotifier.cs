namespace ArchIntel.Api.Realtime;

/// <summary>Relays watcher/job events to connected dashboard clients over ArchitectureHub
/// (05-rest-api.md Section 5.2). An interface (rather than calling IHubContext directly from the
/// Incremental Watcher's integration point or from PlanningEndpoints) so tests can trigger these
/// events without a real watcher process or a real SignalR connection driving them — see
/// ArchIntel.Api.Tests' SignalR integration test, which injects a fake event through this exact
/// seam per 05-rest-api.md Section 9.1.</summary>
public interface IArchitectureChangeNotifier
{
    Task ScanProgressAsync(ScanProgressEvent evt, CancellationToken ct = default);

    Task GraphUpdatedAsync(GraphUpdatedEvent evt, CancellationToken ct = default);

    Task MetricsUpdatedAsync(MetricsUpdatedEvent evt, CancellationToken ct = default);

    Task JobCompletedAsync(JobCompletedEvent evt, CancellationToken ct = default);

    Task JobFailedAsync(JobFailedEvent evt, CancellationToken ct = default);
}
