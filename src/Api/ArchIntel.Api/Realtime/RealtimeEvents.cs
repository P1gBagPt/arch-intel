using ArchIntel.Api.Jobs;

namespace ArchIntel.Api.Realtime;

/// <summary>Payload shapes for the SignalR events in 05-rest-api.md Section 5.3. Kept as plain
/// records (not ASP.NET's ProblemDetails etc.) so they serialize predictably for SignalR clients
/// regardless of server-side type.</summary>
public sealed record ScanProgressEvent(string Phase, int FilesProcessed, int FilesTotal);

public sealed record GraphChangeSummary(int ClassesAdded, int ClassesRemoved, int InterfacesRemoved);

public sealed record GraphUpdatedEvent(
    string ChangeId,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<string> AddedNodeIds,
    IReadOnlyList<string> RemovedNodeIds,
    IReadOnlyList<string> UpdatedNodeIds,
    IReadOnlyList<string> AffectedProjectIds,
    GraphChangeSummary Summary);

public sealed record MetricsTotals(int ClassCount, int ProjectCount);

public sealed record MetricsUpdatedEvent(DateTimeOffset GeneratedAtUtc, MetricsTotals Totals);

public sealed record JobCompletedEvent(string JobId, string Status);

public sealed record JobFailedEvent(string JobId, string Status, JobProblemSummary Problem);
