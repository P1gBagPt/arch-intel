using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;

namespace ArchIntel.McpServer.Tests.Fixtures;

/// <summary>
/// In-memory IGraphReader fake seeded with the README's own Graph Store example chain
/// (04-mcp-server.md Section 8.1): OrderController -> IOrderService -> OrderService ->
/// IOrderRepository -> OrderRepository -> SQL Server.
/// </summary>
public sealed class FixtureGraphReader : IGraphReader
{
    public const string ProjectId = "p_orders";

    public NodeDto OrderController { get; }
    public NodeDto IOrderService { get; }
    public NodeDto OrderService { get; }
    public NodeDto IOrderRepository { get; }
    public NodeDto OrderRepository { get; }
    public NodeDto SqlServer { get; }

    private readonly List<NodeDto> _nodes;
    private readonly List<EdgeDto> _edges = [];
    private ScanMetadataDto? _latestScan = new() { ScanRunId = 4821, CompletedAt = new DateTimeOffset(2026, 7, 24, 2, 11, 0, TimeSpan.Zero) };

    public FixtureGraphReader()
    {
        OrderController = MakeNode("OrderController", NodeType.Controller);
        IOrderService = MakeNode("IOrderService", NodeType.Interface);
        OrderService = MakeNode("OrderService", NodeType.Service);
        IOrderRepository = MakeNode("IOrderRepository", NodeType.Interface);
        OrderRepository = MakeNode("OrderRepository", NodeType.Repository);
        SqlServer = MakeNode("SQL Server", NodeType.ExternalSystem, isExternal: true);

        _nodes = [OrderController, IOrderService, OrderService, IOrderRepository, OrderRepository, SqlServer];

        AddEdge(OrderController, IOrderService, RelationshipType.Calls);
        AddEdge(OrderService, IOrderService, RelationshipType.Implements);
        AddEdge(OrderService, IOrderRepository, RelationshipType.Injects);
        AddEdge(OrderRepository, IOrderRepository, RelationshipType.Implements);
        AddEdge(OrderRepository, SqlServer, RelationshipType.Uses);
    }

    /// <summary>Lets a test simulate "no completed scan yet".</summary>
    public void ClearScanMetadata() => _latestScan = null;

    public Task<NodeDto?> GetNodeAsync(string nodeId, CancellationToken ct = default)
        => Task.FromResult(_nodes.FirstOrDefault(n => n.NodeId == nodeId));

    public Task<IReadOnlyList<NodeDto>> FindByNameAsync(string name, NodeType? nodeType = null, bool exactMatch = false, CancellationToken ct = default)
    {
        IEnumerable<NodeDto> query = _nodes.Where(n => exactMatch
            ? string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)
            : n.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

        if (nodeType is not null)
        {
            query = query.Where(n => n.NodeType == nodeType);
        }

        return Task.FromResult<IReadOnlyList<NodeDto>>(query.ToList());
    }

    public Task<IReadOnlyList<ProjectDto>> ListProjectsAsync(string repoId = "default", CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ProjectDto>>([new ProjectDto { ProjectId = ProjectId, Name = "Orders", Path = "Orders.csproj" }]);

    public Task<IReadOnlyList<NodeDto>> GetNodesByProjectAsync(string projectId, NodeType? nodeType = null, CancellationToken ct = default)
    {
        var query = _nodes.Where(n => n.ProjectId == projectId);
        if (nodeType is not null)
        {
            query = query.Where(n => n.NodeType == nodeType);
        }

        return Task.FromResult<IReadOnlyList<NodeDto>>(query.ToList());
    }

    public Task<IReadOnlyList<EdgeWithNodeDto>> GetDependenciesAsync(string nodeId, RelationshipType? relationshipType = null, CancellationToken ct = default)
    {
        var query = _edges.Where(e => e.SourceId == nodeId);
        if (relationshipType is not null)
        {
            query = query.Where(e => e.RelationshipType == relationshipType);
        }

        var results = query.Select(e => new EdgeWithNodeDto { Edge = e, OtherNode = _nodes.First(n => n.NodeId == e.TargetId) }).ToList();
        return Task.FromResult<IReadOnlyList<EdgeWithNodeDto>>(results);
    }

    public Task<IReadOnlyList<EdgeWithNodeDto>> GetCallersAsync(string nodeId, RelationshipType? relationshipType = null, CancellationToken ct = default)
    {
        var query = _edges.Where(e => e.TargetId == nodeId);
        if (relationshipType is not null)
        {
            query = query.Where(e => e.RelationshipType == relationshipType);
        }

        var results = query.Select(e => new EdgeWithNodeDto { Edge = e, OtherNode = _nodes.First(n => n.NodeId == e.SourceId) }).ToList();
        return Task.FromResult<IReadOnlyList<EdgeWithNodeDto>>(results);
    }

    public Task<ScanMetadataDto?> GetLatestScanMetadataAsync(string repoId = "default", CancellationToken ct = default)
        => Task.FromResult(_latestScan);

    private static NodeDto MakeNode(string name, NodeType type, bool isExternal = false) => new()
    {
        NodeId = $"n_{name.Replace(" ", "").ToLowerInvariant()}",
        ProjectId = ProjectId,
        NodeType = type,
        Name = name,
        FullName = $"Orders.{name}",
        IsExternal = isExternal,
    };

    private void AddEdge(NodeDto source, NodeDto target, RelationshipType type) => _edges.Add(new EdgeDto
    {
        EdgeId = $"e_{source.NodeId}_{target.NodeId}_{type}",
        SourceId = source.NodeId,
        TargetId = target.NodeId,
        RelationshipType = type,
    });
}
