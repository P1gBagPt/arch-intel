using ArchIntel.GraphStore.Contracts.Enums;
using ArchIntel.GraphStore.Core;

namespace ArchIntel.GraphStore.Contracts.Tests;

/// <summary>
/// Phase 2 subgraph/impact traversal contract tests (02-graph-store.md Section 9.1: "extend the
/// contract test suite with impact/neighborhood/subgraph/path cases"). Same SqliteFixture pattern
/// as the Phase 1 contract tests.
/// </summary>
public sealed class GraphReaderPhase2Tests : IAsyncLifetime
{
    private readonly SqliteFixture _fixture = new();
    private readonly IdGenerator _ids = new();
    private ProjectDto _project = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _project = new ProjectDto { ProjectId = _ids.ProjectId("Test.sln", "Orders.csproj"), Name = "Orders", Path = "Orders.csproj" };
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private NodeDto MakeNode(string fullName, NodeType type = NodeType.Class) => new()
    {
        NodeId = _ids.NodeId(_project.ProjectId, null, fullName, type),
        ProjectId = _project.ProjectId,
        NodeType = type,
        Name = fullName.Split('.')[^1],
        FullName = fullName,
    };

    private EdgeDto MakeEdge(NodeDto source, NodeDto target, RelationshipType type) => new()
    {
        EdgeId = _ids.EdgeId(source.NodeId, target.NodeId, type),
        SourceId = source.NodeId,
        TargetId = target.NodeId,
        RelationshipType = type,
    };

    private async Task SeedAsync(IReadOnlyCollection<NodeDto> nodes, IReadOnlyCollection<EdgeDto> edges)
    {
        var scan = await _fixture.Writer.BeginScanAsync(new BeginScanRequest { ScanType = ScanType.Full });
        await _fixture.Writer.UpsertProjectAsync(scan, _project);
        await _fixture.Writer.UpsertNodesAsync(scan, nodes);
        await _fixture.Writer.UpsertEdgesAsync(scan, edges);
        await _fixture.Writer.CompleteScanAsync(scan);
    }

    [Fact]
    public async Task GetImpactAsync_RespectsMaxDepth_ExactlyAtTheBoundary()
    {
        // Linear chain A -> B -> C -> D -> E. The doc's own called-out classic bug is off-by-one
        // depth, so assert the exact node set at each depth boundary, not just "some subset".
        var a = MakeNode("A");
        var b = MakeNode("B");
        var c = MakeNode("C");
        var d = MakeNode("D");
        var e = MakeNode("E");
        await SeedAsync([a, b, c, d, e],
        [
            MakeEdge(a, b, RelationshipType.Calls),
            MakeEdge(b, c, RelationshipType.Calls),
            MakeEdge(c, d, RelationshipType.Calls),
            MakeEdge(d, e, RelationshipType.Calls),
        ]);

        var depth1 = await _fixture.Reader.GetImpactAsync(a.NodeId, maxDepth: 1);
        Assert.Equal([b.NodeId], depth1.AffectedNodes.Select(n => n.NodeId));

        var depth2 = await _fixture.Reader.GetImpactAsync(a.NodeId, maxDepth: 2);
        Assert.Equal(
            new[] { b.NodeId, c.NodeId }.OrderBy(x => x),
            depth2.AffectedNodes.Select(n => n.NodeId).OrderBy(x => x));

        var depthAll = await _fixture.Reader.GetImpactAsync(a.NodeId, maxDepth: 10);
        Assert.Equal(4, depthAll.AffectedNodes.Count);
        Assert.Equal(a.NodeId, depthAll.RootNodeId);
        Assert.DoesNotContain(depthAll.AffectedNodes, n => n.NodeId == a.NodeId);
    }

    [Fact]
    public async Task GetImpactAsync_FiltersByRelationshipType()
    {
        var a = MakeNode("A");
        var b = MakeNode("B");
        var f = MakeNode("F");
        await SeedAsync([a, b, f],
        [
            MakeEdge(a, b, RelationshipType.Calls),
            MakeEdge(a, f, RelationshipType.Injects),
        ]);

        var result = await _fixture.Reader.GetImpactAsync(a.NodeId, maxDepth: 5, relationshipTypes: [RelationshipType.Calls]);

        var affected = Assert.Single(result.AffectedNodes);
        Assert.Equal(b.NodeId, affected.NodeId);
    }

    [Fact]
    public async Task GetImpactAsync_ComputesAffectedByType()
    {
        var a = MakeNode("A");
        var service = MakeNode("Service1", NodeType.Service);
        var repo = MakeNode("Repo1", NodeType.Repository);
        await SeedAsync([a, service, repo],
        [
            MakeEdge(a, service, RelationshipType.Calls),
            MakeEdge(a, repo, RelationshipType.Calls),
        ]);

        var result = await _fixture.Reader.GetImpactAsync(a.NodeId, maxDepth: 1);

        Assert.Equal(1, result.AffectedByType[NodeType.Service]);
        Assert.Equal(1, result.AffectedByType[NodeType.Repository]);
    }

    [Fact]
    public async Task GetTransitiveDependentsAsync_WalksBackward()
    {
        var a = MakeNode("A");
        var b = MakeNode("B");
        var c = MakeNode("C");
        await SeedAsync([a, b, c],
        [
            MakeEdge(a, b, RelationshipType.Calls),
            MakeEdge(b, c, RelationshipType.Calls),
        ]);

        var oneHop = await _fixture.Reader.GetTransitiveDependentsAsync(c.NodeId, maxDepth: 1);
        Assert.Equal([b.NodeId], oneHop.AffectedNodes.Select(n => n.NodeId));

        var allHops = await _fixture.Reader.GetTransitiveDependentsAsync(c.NodeId, maxDepth: 10);
        Assert.Equal(
            new[] { a.NodeId, b.NodeId }.OrderBy(x => x),
            allHops.AffectedNodes.Select(n => n.NodeId).OrderBy(x => x));
    }

    [Fact]
    public async Task GetNeighborhoodAsync_TruncatesAtMaxNodes()
    {
        var seed = MakeNode("Seed");
        var neighbors = Enumerable.Range(0, 10).Select(i => MakeNode($"Neighbor{i}")).ToList();
        await SeedAsync([seed, .. neighbors], neighbors.Select(n => MakeEdge(seed, n, RelationshipType.Calls)).ToList());

        var result = await _fixture.Reader.GetNeighborhoodAsync(new GetNeighborhoodRequest { SeedNodeId = seed.NodeId, Depth = 1, MaxNodes = 5 });

        Assert.True(result.Truncated);
        Assert.Equal(5, result.Nodes.Count);
        Assert.Contains(result.Nodes, n => n.NodeId == seed.NodeId);
    }

    [Fact]
    public async Task GetNeighborhoodAsync_IncludesEdgesAmongVisibleNodes()
    {
        var a = MakeNode("A");
        var b = MakeNode("B");
        var c = MakeNode("C");
        await SeedAsync([a, b, c],
        [
            MakeEdge(a, b, RelationshipType.Calls),
            MakeEdge(b, c, RelationshipType.Calls),
        ]);

        var result = await _fixture.Reader.GetNeighborhoodAsync(new GetNeighborhoodRequest { SeedNodeId = b.NodeId, Depth = 1, MaxNodes = 10 });

        Assert.Equal(3, result.Nodes.Count);
        Assert.Equal(2, result.Edges.Count);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task GetSubgraphAsync_FiltersByProjectAndNodeType()
    {
        var otherProject = new ProjectDto { ProjectId = _ids.ProjectId("Test.sln", "Other.csproj"), Name = "Other", Path = "Other.csproj" };
        var x = MakeNode("X", NodeType.Class);
        var y = MakeNode("Y", NodeType.Service);
        var z = new NodeDto
        {
            NodeId = _ids.NodeId(otherProject.ProjectId, null, "Z", NodeType.Class),
            ProjectId = otherProject.ProjectId,
            NodeType = NodeType.Class,
            Name = "Z",
            FullName = "Z",
        };

        var scan = await _fixture.Writer.BeginScanAsync(new BeginScanRequest { ScanType = ScanType.Full });
        await _fixture.Writer.UpsertProjectAsync(scan, _project);
        await _fixture.Writer.UpsertProjectAsync(scan, otherProject);
        await _fixture.Writer.UpsertNodesAsync(scan, [x, y, z]);
        await _fixture.Writer.CompleteScanAsync(scan);

        var byProject = await _fixture.Reader.GetSubgraphAsync(new GetSubgraphRequest { ProjectIds = [_project.ProjectId] });
        Assert.Equal(
            new[] { x.NodeId, y.NodeId }.OrderBy(v => v),
            byProject.Nodes.Select(n => n.NodeId).OrderBy(v => v));

        var byType = await _fixture.Reader.GetSubgraphAsync(new GetSubgraphRequest { NodeTypes = [NodeType.Service] });
        Assert.Equal(y.NodeId, Assert.Single(byType.Nodes).NodeId);
    }

    [Fact]
    public async Task GetSubgraphAsync_PaginatesAndReportsTruncation()
    {
        var nodes = Enumerable.Range(0, 5).Select(i => MakeNode($"N{i}")).ToList();
        await SeedAsync(nodes, []);

        var page0 = await _fixture.Reader.GetSubgraphAsync(new GetSubgraphRequest { ProjectIds = [_project.ProjectId], Page = 0, PageSize = 2, MaxNodes = 2 });
        var page1 = await _fixture.Reader.GetSubgraphAsync(new GetSubgraphRequest { ProjectIds = [_project.ProjectId], Page = 1, PageSize = 2, MaxNodes = 2 });

        Assert.Equal(2, page0.Nodes.Count);
        Assert.True(page0.Truncated);
        Assert.Equal(2, page1.Nodes.Count);
        Assert.Empty(page0.Nodes.Select(n => n.NodeId).Intersect(page1.Nodes.Select(n => n.NodeId)));
    }

    [Fact]
    public async Task FindPathsAsync_FindsAllSimplePaths_AndDoesNotLoopOnACycle()
    {
        // Diamond A -> B -> D and A -> C -> D, plus a cycle edge D -> A. FindPaths must terminate
        // promptly (not hang) and must not report a "path" that revisits A.
        var a = MakeNode("A");
        var b = MakeNode("B");
        var c = MakeNode("C");
        var d = MakeNode("D");
        await SeedAsync([a, b, c, d],
        [
            MakeEdge(a, b, RelationshipType.Calls),
            MakeEdge(a, c, RelationshipType.Calls),
            MakeEdge(b, d, RelationshipType.Calls),
            MakeEdge(c, d, RelationshipType.Calls),
            MakeEdge(d, a, RelationshipType.Calls),
        ]);

        var paths = await _fixture.Reader.FindPathsAsync(a.NodeId, d.NodeId, maxDepth: 4);

        Assert.Equal(2, paths.Count);
        Assert.All(paths, p => Assert.Equal(a.NodeId, p.NodeIds[0]));
        Assert.All(paths, p => Assert.Equal(d.NodeId, p.NodeIds[^1]));
        Assert.All(paths, p => Assert.DoesNotContain(a.NodeId, p.NodeIds.Skip(1)));
        Assert.Contains(paths, p => p.NodeIds.SequenceEqual([a.NodeId, b.NodeId, d.NodeId]));
        Assert.Contains(paths, p => p.NodeIds.SequenceEqual([a.NodeId, c.NodeId, d.NodeId]));
    }

    [Fact]
    public async Task FindPathsAsync_ReturnsEmpty_WhenNoPathExists()
    {
        var a = MakeNode("A");
        var isolated = MakeNode("Isolated");
        await SeedAsync([a, isolated], []);

        var paths = await _fixture.Reader.FindPathsAsync(a.NodeId, isolated.NodeId, maxDepth: 5);

        Assert.Empty(paths);
    }
}
