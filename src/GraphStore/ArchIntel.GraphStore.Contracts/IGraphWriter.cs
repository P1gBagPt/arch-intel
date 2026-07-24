namespace ArchIntel.GraphStore.Contracts;

public interface IGraphWriter
{
    /// <summary>
    /// Starts a scan run. Must be called before any Upsert* calls in a scan.
    /// Returns a scan handle carrying the scan_run_id used to stamp all writes in this run.
    /// </summary>
    Task<ScanHandle> BeginScanAsync(BeginScanRequest request, CancellationToken ct = default);

    Task UpsertProjectAsync(ScanHandle scan, ProjectDto project, CancellationToken ct = default);

    /// <summary>Upsert is keyed by NodeDto.NodeId. Existing row is updated in place; new row is inserted otherwise.</summary>
    Task UpsertNodeAsync(ScanHandle scan, NodeDto node, CancellationToken ct = default);

    /// <summary>Batch variant — required for full scans touching thousands of nodes; implementations MUST batch internally.</summary>
    Task UpsertNodesAsync(ScanHandle scan, IReadOnlyCollection<NodeDto> nodes, CancellationToken ct = default);

    Task UpsertEdgeAsync(ScanHandle scan, EdgeDto edge, CancellationToken ct = default);

    Task UpsertEdgesAsync(ScanHandle scan, IReadOnlyCollection<EdgeDto> edges, CancellationToken ct = default);

    /// <summary>
    /// Marks the scan as complete. For a Full scan, any node/edge whose scan_version
    /// is older than this scan and belongs to this repo_id is considered stale and removed.
    /// </summary>
    Task CompleteScanAsync(ScanHandle scan, CancellationToken ct = default);

    Task FailScanAsync(ScanHandle scan, string errorMessage, CancellationToken ct = default);
}

public sealed record BeginScanRequest
{
    public string RepoId { get; init; } = "default";
    public required ScanType ScanType { get; init; }
    public string? TriggeredBy { get; init; }
    public IReadOnlyCollection<string>? ChangedFiles { get; init; }
}

public enum ScanType { Full, Incremental }

public sealed record ScanHandle
{
    public required long ScanRunId { get; init; }
    public required string RepoId { get; init; }
    public required ScanType ScanType { get; init; }
}
