using ArchIntel.GraphStore.Contracts.Enums;
using ArchIntel.GraphStore.Contracts.Exceptions;
using ArchIntel.GraphStore.Core;

namespace ArchIntel.GraphStore.Contracts.Tests;

/// <summary>
/// The shared contract test suite (Section 9.1 of 02-graph-store.md): written once against
/// IGraphWriter/IGraphReader, run against the SQLite backend here (and, in later phases, any
/// other backend implementing the same contract).
/// </summary>
public class GraphWriterReaderContractTests : IAsyncLifetime
{
    private readonly SqliteFixture _fixture = new();
    private readonly IdGenerator _ids = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private ProjectDto MakeProject(string name = "Orders") => new()
    {
        ProjectId = _ids.ProjectId("Test.sln", $"{name}.csproj"),
        Name = name,
        Path = $"{name}.csproj",
    };

    private NodeDto MakeNode(string projectId, string fullName, NodeType type = NodeType.Class, IReadOnlyDictionary<string, string>? metadata = null) => new()
    {
        NodeId = _ids.NodeId(projectId, null, fullName, type),
        ProjectId = projectId,
        NodeType = type,
        Name = fullName.Split('.')[^1],
        FullName = fullName,
        Metadata = metadata ?? new Dictionary<string, string>(),
    };

    [Fact]
    public async Task UpsertingSameNodeTwice_WithDifferentMetadata_ResultsInOneRow_WithUpdatedMetadata()
    {
        var project = MakeProject();
        var scan1 = await _fixture.Writer.BeginScanAsync(new BeginScanRequest { ScanType = ScanType.Full });
        await _fixture.Writer.UpsertProjectAsync(scan1, project);

        var node = MakeNode(project.ProjectId, "Orders.OrderService", metadata: new Dictionary<string, string> { ["v"] = "1" });
        await _fixture.Writer.UpsertNodeAsync(scan1, node);
        await _fixture.Writer.CompleteScanAsync(scan1);

        var scan2 = await _fixture.Writer.BeginScanAsync(new BeginScanRequest { ScanType = ScanType.Full });
        await _fixture.Writer.UpsertProjectAsync(scan2, project);
        var updatedNode = node with { Metadata = new Dictionary<string, string> { ["v"] = "2" } };
        await _fixture.Writer.UpsertNodeAsync(scan2, updatedNode);
        await _fixture.Writer.CompleteScanAsync(scan2);

        var fetched = await _fixture.Reader.GetNodeAsync(node.NodeId);
        Assert.NotNull(fetched);
        Assert.Equal("2", fetched!.Metadata["v"]);

        var allWithName = await _fixture.Reader.FindByNameAsync("OrderService", exactMatch: true);
        Assert.Single(allWithName);
    }

    [Fact]
    public async Task CompleteScanAsync_OnFullScan_RemovesNodesAndEdges_NotTouchedByThatScan()
    {
        var project = MakeProject();
        var scan1 = await _fixture.Writer.BeginScanAsync(new BeginScanRequest { ScanType = ScanType.Full });
        await _fixture.Writer.UpsertProjectAsync(scan1, project);

        var staleNode = MakeNode(project.ProjectId, "Orders.StaleService");
        var keptNode = MakeNode(project.ProjectId, "Orders.KeptService");
        await _fixture.Writer.UpsertNodesAsync(scan1, [staleNode, keptNode]);

        var edge = new EdgeDto
        {
            EdgeId = _ids.EdgeId(keptNode.NodeId, staleNode.NodeId, RelationshipType.Uses),
            SourceId = keptNode.NodeId,
            TargetId = staleNode.NodeId,
            RelationshipType = RelationshipType.Uses,
        };
        await _fixture.Writer.UpsertEdgeAsync(scan1, edge);
        await _fixture.Writer.CompleteScanAsync(scan1);

        // Second full scan only re-touches keptNode — staleNode (and the edge into it) should be swept.
        var scan2 = await _fixture.Writer.BeginScanAsync(new BeginScanRequest { ScanType = ScanType.Full });
        await _fixture.Writer.UpsertProjectAsync(scan2, project);
        await _fixture.Writer.UpsertNodeAsync(scan2, keptNode);
        await _fixture.Writer.CompleteScanAsync(scan2);

        Assert.Null(await _fixture.Reader.GetNodeAsync(staleNode.NodeId));
        Assert.NotNull(await _fixture.Reader.GetNodeAsync(keptNode.NodeId));

        var deps = await _fixture.Reader.GetDependenciesAsync(keptNode.NodeId);
        Assert.Empty(deps);
    }

    [Fact]
    public async Task GetDependenciesAndGetCallers_ReturnCorrect1HopResults_AndEmptyListsForLeafRootNodes()
    {
        var project = MakeProject();
        var scan = await _fixture.Writer.BeginScanAsync(new BeginScanRequest { ScanType = ScanType.Full });
        await _fixture.Writer.UpsertProjectAsync(scan, project);

        var controller = MakeNode(project.ProjectId, "Orders.OrderController", NodeType.Controller);
        var service = MakeNode(project.ProjectId, "Orders.OrderService", NodeType.Service);
        var repository = MakeNode(project.ProjectId, "Orders.OrderRepository", NodeType.Repository);
        await _fixture.Writer.UpsertNodesAsync(scan, [controller, service, repository]);

        var controllerCallsService = new EdgeDto
        {
            EdgeId = _ids.EdgeId(controller.NodeId, service.NodeId, RelationshipType.Calls),
            SourceId = controller.NodeId,
            TargetId = service.NodeId,
            RelationshipType = RelationshipType.Calls,
        };
        var serviceCallsRepository = new EdgeDto
        {
            EdgeId = _ids.EdgeId(service.NodeId, repository.NodeId, RelationshipType.Calls),
            SourceId = service.NodeId,
            TargetId = repository.NodeId,
            RelationshipType = RelationshipType.Calls,
        };
        var serviceInjectsRepository = new EdgeDto
        {
            EdgeId = _ids.EdgeId(service.NodeId, repository.NodeId, RelationshipType.Injects),
            SourceId = service.NodeId,
            TargetId = repository.NodeId,
            RelationshipType = RelationshipType.Injects,
        };
        await _fixture.Writer.UpsertEdgesAsync(scan, [controllerCallsService, serviceCallsRepository, serviceInjectsRepository]);
        await _fixture.Writer.CompleteScanAsync(scan);

        var serviceDependencies = await _fixture.Reader.GetDependenciesAsync(service.NodeId);
        Assert.Equal(2, serviceDependencies.Count);

        // Filtering by relationship type narrows the 2-edge (Calls + Injects) fan-out to one.
        var serviceCallDependencies = await _fixture.Reader.GetDependenciesAsync(service.NodeId, RelationshipType.Calls);
        Assert.Single(serviceCallDependencies);
        Assert.Equal(repository.NodeId, serviceCallDependencies[0].OtherNode.NodeId);
        Assert.Equal(RelationshipType.Calls, serviceCallDependencies[0].Edge.RelationshipType);

        var serviceCallers = await _fixture.Reader.GetCallersAsync(service.NodeId);
        Assert.Single(serviceCallers);
        Assert.Equal(controller.NodeId, serviceCallers[0].OtherNode.NodeId);

        // Leaf node: no outgoing dependencies.
        Assert.Empty(await _fixture.Reader.GetDependenciesAsync(repository.NodeId));

        // Root node: no incoming callers.
        Assert.Empty(await _fixture.Reader.GetCallersAsync(controller.NodeId));
    }

    [Fact]
    public async Task ConcurrentBeginScan_ForSameRepoId_ThrowsScanConflictException()
    {
        await _fixture.Writer.BeginScanAsync(new BeginScanRequest { ScanType = ScanType.Full });

        await Assert.ThrowsAsync<ScanConflictException>(() =>
            _fixture.Writer.BeginScanAsync(new BeginScanRequest { ScanType = ScanType.Full }));
    }

    [Fact]
    public async Task ListProjectsAsync_ReturnsUpsertedProjects()
    {
        var project = MakeProject("Payments");
        var scan = await _fixture.Writer.BeginScanAsync(new BeginScanRequest { ScanType = ScanType.Full });
        await _fixture.Writer.UpsertProjectAsync(scan, project);
        await _fixture.Writer.CompleteScanAsync(scan);

        var projects = await _fixture.Reader.ListProjectsAsync();
        Assert.Contains(projects, p => p.ProjectId == project.ProjectId && p.Name == "Payments");
    }

    [Fact]
    public async Task UpsertNode_StoresNodeType_AsItsReadableName_NotItsOrdinal()
    {
        // Regression guard: Dapper does not route enum properties through a registered
        // TypeHandler on the batched-parameter INSERT path, so it's easy to accidentally
        // persist the enum's underlying int (e.g. "21") instead of "Service". The migration
        // schema documents node_type/relationship_type as human-readable TEXT — verify that.
        var project = MakeProject();
        var scan = await _fixture.Writer.BeginScanAsync(new BeginScanRequest { ScanType = ScanType.Full });
        await _fixture.Writer.UpsertProjectAsync(scan, project);
        var node = MakeNode(project.ProjectId, "Orders.OrderService", NodeType.Service);
        await _fixture.Writer.UpsertNodeAsync(scan, node);
        await _fixture.Writer.CompleteScanAsync(scan);

        using var connection = await _fixture.ConnectionFactory.OpenConnectionAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT node_type FROM nodes WHERE node_id = @NodeId";
        var param = cmd.CreateParameter();
        param.ParameterName = "@NodeId";
        param.Value = node.NodeId;
        cmd.Parameters.Add(param);

        var rawValue = (string)(await Task.FromResult(cmd.ExecuteScalar()))!;
        Assert.Equal("Service", rawValue);
    }

    [Fact]
    public async Task GetNodesByProjectAsync_FiltersByNodeType()
    {
        var project = MakeProject();
        var scan = await _fixture.Writer.BeginScanAsync(new BeginScanRequest { ScanType = ScanType.Full });
        await _fixture.Writer.UpsertProjectAsync(scan, project);

        var service = MakeNode(project.ProjectId, "Orders.OrderService", NodeType.Service);
        var controller = MakeNode(project.ProjectId, "Orders.OrderController", NodeType.Controller);
        await _fixture.Writer.UpsertNodesAsync(scan, [service, controller]);
        await _fixture.Writer.CompleteScanAsync(scan);

        var services = await _fixture.Reader.GetNodesByProjectAsync(project.ProjectId, NodeType.Service);
        Assert.Single(services);
        Assert.Equal(service.NodeId, services[0].NodeId);
    }

    [Fact]
    public async Task GetLatestScanMetadataAsync_ReturnsNull_WhenNoCompletedScans()
    {
        var metadata = await _fixture.Reader.GetLatestScanMetadataAsync();

        Assert.Null(metadata);
    }

    [Fact]
    public async Task GetLatestScanMetadataAsync_ReturnsMostRecentCompletedScan()
    {
        var scan1 = await _fixture.Writer.BeginScanAsync(new BeginScanRequest { ScanType = ScanType.Full });
        await _fixture.Writer.CompleteScanAsync(scan1);

        var scan2 = await _fixture.Writer.BeginScanAsync(new BeginScanRequest { ScanType = ScanType.Full });
        await _fixture.Writer.CompleteScanAsync(scan2);

        var metadata = await _fixture.Reader.GetLatestScanMetadataAsync();

        Assert.NotNull(metadata);
        Assert.Equal(scan2.ScanRunId, metadata!.ScanRunId);
        Assert.True(metadata.CompletedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }
}
