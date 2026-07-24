using System.Diagnostics;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchIntel.GraphStore.Core;

namespace ArchIntel.GraphStore.Contracts.Tests;

/// <summary>
/// Load/perf smoke test for the Phase 2 traversal queries (02-graph-store.md Section 9.4 / Section
/// 11's "load/perf test subgraph and impact queries against a synthetic ... node fixture"). Scaled
/// down from the doc's stated 10k-50k nodes to a few thousand so this stays a routine, fast part of
/// `dotnet test` rather than a multi-minute addition to every run — still large/branchy enough to
/// exercise MaxNodes truncation and give a real (if not rigorous) latency signal.
///
/// The fixture is a layered DAG (each node fans out to a handful of nodes in the next layer) rather
/// than a chain or a complete graph: it's branchy enough that a naive path-tracking traversal would
/// explode combinatorially, which is exactly why GetImpactAsync/GetNeighborhoodAsync deliberately
/// dedupe on (node_id, depth) instead of tracking full path history (see SqliteGraphReader.Traversal.cs).
/// </summary>
public sealed class GraphReaderPerformanceTests : IAsyncLifetime
{
    private const int LayerCount = 20;
    private const int LayerWidth = 100;
    private const int FanOut = 5;

    private readonly SqliteFixture _fixture = new();
    private readonly IdGenerator _ids = new();
    private string _seedNodeId = null!;
    private string _midGraphNodeId = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();

        var project = new ProjectDto { ProjectId = _ids.ProjectId("Test.sln", "Perf.csproj"), Name = "Perf", Path = "Perf.csproj" };

        var layers = new List<List<NodeDto>>();
        for (var layer = 0; layer < LayerCount; layer++)
        {
            var nodes = Enumerable.Range(0, LayerWidth)
                .Select(i => MakeNode(project.ProjectId, $"L{layer}N{i}"))
                .ToList();
            layers.Add(nodes);
        }

        _seedNodeId = layers[0][0].NodeId;
        _midGraphNodeId = layers[LayerCount / 2][0].NodeId;

        var edges = new List<EdgeDto>();
        for (var layer = 0; layer < LayerCount - 1; layer++)
        {
            for (var i = 0; i < LayerWidth; i++)
            {
                for (var f = 0; f < FanOut; f++)
                {
                    var target = layers[layer + 1][(i + f) % LayerWidth];
                    edges.Add(MakeEdge(layers[layer][i], target));
                }
            }
        }

        var scan = await _fixture.Writer.BeginScanAsync(new BeginScanRequest { ScanType = ScanType.Full });
        await _fixture.Writer.UpsertProjectAsync(scan, project);
        await _fixture.Writer.UpsertNodesAsync(scan, layers.SelectMany(l => l).ToList());
        await _fixture.Writer.UpsertEdgesAsync(scan, edges);
        await _fixture.Writer.CompleteScanAsync(scan);
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private NodeDto MakeNode(string projectId, string fullName) => new()
    {
        NodeId = _ids.NodeId(projectId, null, fullName, NodeType.Class),
        ProjectId = projectId,
        NodeType = NodeType.Class,
        Name = fullName,
        FullName = fullName,
    };

    private EdgeDto MakeEdge(NodeDto source, NodeDto target) => new()
    {
        EdgeId = _ids.EdgeId(source.NodeId, target.NodeId, RelationshipType.Calls),
        SourceId = source.NodeId,
        TargetId = target.NodeId,
        RelationshipType = RelationshipType.Calls,
    };

    [Fact]
    public async Task GetImpactAsync_OnA2000NodeGraph_CompletesWithinBudget_AndFindsAffectedNodes()
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await _fixture.Reader.GetImpactAsync(_seedNodeId, maxDepth: 10);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"GetImpactAsync took {stopwatch.Elapsed} on a {LayerCount * LayerWidth}-node graph");
        Assert.NotEmpty(result.AffectedNodes);
        Assert.True(result.AffectedNodes.Count <= LayerCount * LayerWidth);
    }

    [Fact]
    public async Task GetNeighborhoodAsync_OnABranchyGraph_TruncatesAtMaxNodes_WithinBudget()
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await _fixture.Reader.GetNeighborhoodAsync(new GetNeighborhoodRequest
        {
            SeedNodeId = _midGraphNodeId,
            Depth = 3,
            MaxNodes = 50,
        });
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"GetNeighborhoodAsync took {stopwatch.Elapsed}");
        Assert.True(result.Truncated);
        Assert.Equal(50, result.Nodes.Count);
    }
}
