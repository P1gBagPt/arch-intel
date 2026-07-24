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

    public Task<ImpactResultDto> GetImpactAsync(string nodeId, int maxDepth = 10, IReadOnlyCollection<RelationshipType>? relationshipTypes = null, CancellationToken ct = default)
        => Task.FromResult(BuildImpact(nodeId, maxDepth, relationshipTypes, forward: true));

    public Task<ImpactResultDto> GetTransitiveDependentsAsync(string nodeId, int maxDepth = 10, IReadOnlyCollection<RelationshipType>? relationshipTypes = null, CancellationToken ct = default)
        => Task.FromResult(BuildImpact(nodeId, maxDepth, relationshipTypes, forward: false));

    public Task<SubgraphDto> GetNeighborhoodAsync(GetNeighborhoodRequest request, CancellationToken ct = default)
    {
        var forward = Bfs(request.SeedNodeId, request.Depth, request.RelationshipTypes, forward: true);
        var reverse = Bfs(request.SeedNodeId, request.Depth, request.RelationshipTypes, forward: false);
        var merged = new Dictionary<string, int>(forward);
        foreach (var (id, depth) in reverse)
        {
            if (!merged.TryGetValue(id, out var existing) || depth < existing)
            {
                merged[id] = depth;
            }
        }

        merged[request.SeedNodeId] = 0;

        IEnumerable<NodeDto> candidates = _nodes.Where(n => merged.ContainsKey(n.NodeId));
        if (request.NodeTypes is { Count: > 0 } types)
        {
            candidates = candidates.Where(n => types.Contains(n.NodeType));
        }

        if (!request.IncludeExternal)
        {
            candidates = candidates.Where(n => !n.IsExternal);
        }

        var ordered = candidates.OrderBy(n => merged[n.NodeId]).ThenBy(n => n.FullName, StringComparer.OrdinalIgnoreCase).ToList();
        var truncated = ordered.Count > request.MaxNodes;
        var finalNodes = ordered.Take(request.MaxNodes).ToList();
        var nodeIds = finalNodes.Select(n => n.NodeId).ToHashSet();
        var edges = _edges.Where(e => nodeIds.Contains(e.SourceId) && nodeIds.Contains(e.TargetId)).ToList();

        return Task.FromResult(new SubgraphDto { Nodes = finalNodes, Edges = edges, Truncated = truncated });
    }

    public Task<SubgraphDto> GetSubgraphAsync(GetSubgraphRequest request, CancellationToken ct = default)
    {
        IEnumerable<NodeDto> query = _nodes;
        if (request.ProjectIds is { Count: > 0 } projectIds)
        {
            query = query.Where(n => projectIds.Contains(n.ProjectId));
        }

        if (request.NodeTypes is { Count: > 0 } types)
        {
            query = query.Where(n => types.Contains(n.NodeType));
        }

        var all = query.OrderBy(n => n.FullName, StringComparer.OrdinalIgnoreCase).ToList();
        var effectivePageSize = Math.Max(1, Math.Min(request.PageSize, request.MaxNodes));
        var page = all.Skip(request.Page * request.PageSize).Take(effectivePageSize + 1).ToList();
        var truncated = page.Count > effectivePageSize;
        var finalNodes = page.Take(effectivePageSize).ToList();
        var nodeIds = finalNodes.Select(n => n.NodeId).ToHashSet();
        var edges = _edges.Where(e => nodeIds.Contains(e.SourceId) && nodeIds.Contains(e.TargetId)).ToList();

        return Task.FromResult(new SubgraphDto { Nodes = finalNodes, Edges = edges, Truncated = truncated });
    }

    public Task<IReadOnlyList<PathDto>> FindPathsAsync(string sourceNodeId, string targetNodeId, int maxDepth = 8, CancellationToken ct = default)
    {
        var paths = new List<PathDto>();
        void Dfs(string current, List<string> nodePath, List<string> edgePath)
        {
            if (nodePath.Count - 1 > maxDepth || paths.Count >= 20)
            {
                return;
            }

            if (current == targetNodeId && nodePath.Count > 1)
            {
                paths.Add(new PathDto { NodeIds = [.. nodePath], EdgeIds = [.. edgePath] });
                return;
            }

            foreach (var edge in _edges.Where(e => e.SourceId == current && !nodePath.Contains(e.TargetId)))
            {
                nodePath.Add(edge.TargetId);
                edgePath.Add(edge.EdgeId);
                Dfs(edge.TargetId, nodePath, edgePath);
                nodePath.RemoveAt(nodePath.Count - 1);
                edgePath.RemoveAt(edgePath.Count - 1);
            }
        }

        Dfs(sourceNodeId, [sourceNodeId], []);
        return Task.FromResult<IReadOnlyList<PathDto>>(paths);
    }

    private ImpactResultDto BuildImpact(string nodeId, int maxDepth, IReadOnlyCollection<RelationshipType>? relationshipTypes, bool forward)
    {
        var reachable = Bfs(nodeId, maxDepth, relationshipTypes, forward);
        reachable.Remove(nodeId);

        var affectedNodes = _nodes.Where(n => reachable.ContainsKey(n.NodeId)).ToList();
        var affectedByType = affectedNodes.GroupBy(n => n.NodeType).ToDictionary(g => g.Key, g => g.Count());

        return new ImpactResultDto
        {
            RootNodeId = nodeId,
            AffectedNodes = affectedNodes,
            SamplePaths = [],
            AffectedByType = affectedByType,
        };
    }

    private Dictionary<string, int> Bfs(string startNodeId, int maxDepth, IReadOnlyCollection<RelationshipType>? relationshipTypes, bool forward)
    {
        var visited = new Dictionary<string, int> { [startNodeId] = 0 };
        var frontier = new Queue<(string NodeId, int Depth)>();
        frontier.Enqueue((startNodeId, 0));

        while (frontier.Count > 0)
        {
            var (current, depth) = frontier.Dequeue();
            if (depth >= maxDepth)
            {
                continue;
            }

            var candidateEdges = forward
                ? _edges.Where(e => e.SourceId == current)
                : _edges.Where(e => e.TargetId == current);
            if (relationshipTypes is { Count: > 0 })
            {
                candidateEdges = candidateEdges.Where(e => relationshipTypes.Contains(e.RelationshipType));
            }

            foreach (var edge in candidateEdges)
            {
                var next = forward ? edge.TargetId : edge.SourceId;
                if (!visited.ContainsKey(next))
                {
                    visited[next] = depth + 1;
                    frontier.Enqueue((next, depth + 1));
                }
            }
        }

        return visited;
    }

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
