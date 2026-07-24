using System.Collections.Concurrent;
using System.Text.Json;
using ArchIntel.GraphStore.Contracts;

namespace ArchScanner.Core.Output;

/// <summary>
/// Reference IGraphWriter implementation writing newline-delimited JSON (Section 4.5) — for
/// debugging, CI golden-file tests, and any consumer not yet ready to implement IGraphWriter
/// against a real database. Not a second contract: it's IGraphWriter implemented against the
/// filesystem, using the exact same DTOs.
/// </summary>
public sealed class NdjsonGraphWriter : IGraphWriter
{
    private readonly string _outputDirectory;
    private readonly ConcurrentDictionary<long, ScanState> _scans = new();
    private long _nextScanRunId;

    public NdjsonGraphWriter(string outputDirectory)
    {
        _outputDirectory = outputDirectory;
    }

    public Task<ScanHandle> BeginScanAsync(BeginScanRequest request, CancellationToken ct = default)
    {
        var scanRunId = Interlocked.Increment(ref _nextScanRunId);
        _scans[scanRunId] = new ScanState(DateTimeOffset.UtcNow);
        return Task.FromResult(new ScanHandle { ScanRunId = scanRunId, RepoId = request.RepoId, ScanType = request.ScanType });
    }

    public Task UpsertProjectAsync(ScanHandle scan, ProjectDto project, CancellationToken ct = default)
    {
        State(scan).Projects[project.ProjectId] = project;
        return Task.CompletedTask;
    }

    public Task UpsertNodeAsync(ScanHandle scan, NodeDto node, CancellationToken ct = default)
    {
        State(scan).Nodes[node.NodeId] = node;
        return Task.CompletedTask;
    }

    public Task UpsertNodesAsync(ScanHandle scan, IReadOnlyCollection<NodeDto> nodes, CancellationToken ct = default)
    {
        var state = State(scan);
        foreach (var node in nodes)
        {
            state.Nodes[node.NodeId] = node;
        }

        return Task.CompletedTask;
    }

    public Task UpsertEdgeAsync(ScanHandle scan, EdgeDto edge, CancellationToken ct = default)
    {
        State(scan).Edges[edge.EdgeId] = edge;
        return Task.CompletedTask;
    }

    public Task UpsertEdgesAsync(ScanHandle scan, IReadOnlyCollection<EdgeDto> edges, CancellationToken ct = default)
    {
        var state = State(scan);
        foreach (var edge in edges)
        {
            state.Edges[edge.EdgeId] = edge;
        }

        return Task.CompletedTask;
    }

    public async Task CompleteScanAsync(ScanHandle scan, CancellationToken ct = default)
    {
        var state = State(scan);
        Directory.CreateDirectory(_outputDirectory);

        await WriteNdjsonAsync(Path.Combine(_outputDirectory, "projects.ndjson"), state.Projects.Values, ct);
        await WriteNdjsonAsync(Path.Combine(_outputDirectory, "nodes.ndjson"), state.Nodes.Values, ct);
        await WriteNdjsonAsync(Path.Combine(_outputDirectory, "edges.ndjson"), state.Edges.Values, ct);

        var manifest = new
        {
            scanRunId = scan.ScanRunId,
            repoId = scan.RepoId,
            scanType = scan.ScanType.ToString(),
            startedAtUtc = state.StartedAtUtc,
            completedAtUtc = DateTimeOffset.UtcNow,
            projectCount = state.Projects.Count,
            nodeCount = state.Nodes.Count,
            edgeCount = state.Edges.Count,
        };

        await File.WriteAllTextAsync(
            Path.Combine(_outputDirectory, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            ct);

        _scans.TryRemove(scan.ScanRunId, out _);
    }

    public Task FailScanAsync(ScanHandle scan, string errorMessage, CancellationToken ct = default)
    {
        _scans.TryRemove(scan.ScanRunId, out _);
        return Task.CompletedTask;
    }

    private ScanState State(ScanHandle scan) => _scans[scan.ScanRunId];

    private static async Task WriteNdjsonAsync<T>(string path, IEnumerable<T> items, CancellationToken ct)
    {
        await using var stream = File.Create(path);
        await using var writer = new StreamWriter(stream);
        foreach (var item in items)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(item));
        }
    }

    private sealed class ScanState(DateTimeOffset startedAtUtc)
    {
        public DateTimeOffset StartedAtUtc { get; } = startedAtUtc;
        public ConcurrentDictionary<string, ProjectDto> Projects { get; } = new();
        public ConcurrentDictionary<string, NodeDto> Nodes { get; } = new();
        public ConcurrentDictionary<string, EdgeDto> Edges { get; } = new();
    }
}
