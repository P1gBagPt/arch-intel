using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using Dapper;

namespace ArchIntel.GraphStore.Sqlite;

/// <summary>
/// Phase 2 subgraph/impact traversal (02-graph-store.md Section 5). Split into its own partial-class
/// file to keep the Phase 1 reader (SqliteGraphReader.cs) navigable as its own thing.
///
/// GetImpactAsync/GetTransitiveDependentsAsync/GetNeighborhoodAsync deliberately mirror the doc's own
/// Section 5.2 reference SQL — a plain depth-bounded recursive CTE with no mid-recursion cycle
/// collapsing — rather than a fancier visited-set traversal. That's the risk the doc's own risk
/// table (#3) explicitly accepts, mitigated by capping maxDepth server-side rather than by more
/// elaborate traversal logic. FindPathsAsync (and the SamplePaths reconstruction it shares with the
/// impact queries) is the one place a *path*, not just reachability, is the actual output, so that
/// one CTE tracks path history as a pipe-delimited string (SQLite has no array type) with
/// instr()-based cycle prevention — a path that revisits a node isn't a simple path.
/// </summary>
public sealed partial class SqliteGraphReader
{
    private const int MaxImpactDepth = 10;
    private const int MaxNeighborhoodDepth = 5;
    private const int MaxPathDepth = 8;
    private const int SamplePathLimit = 5;
    private const int MaxPathResults = 20;

    private enum TraversalDirection { Forward, Reverse }

    public Task<ImpactResultDto> GetImpactAsync(string nodeId, int maxDepth = 10, IReadOnlyCollection<RelationshipType>? relationshipTypes = null, CancellationToken ct = default)
        => GetImpactCoreAsync(nodeId, maxDepth, relationshipTypes, TraversalDirection.Forward, ct);

    public Task<ImpactResultDto> GetTransitiveDependentsAsync(string nodeId, int maxDepth = 10, IReadOnlyCollection<RelationshipType>? relationshipTypes = null, CancellationToken ct = default)
        => GetImpactCoreAsync(nodeId, maxDepth, relationshipTypes, TraversalDirection.Reverse, ct);

    private async Task<ImpactResultDto> GetImpactCoreAsync(
        string nodeId, int maxDepth, IReadOnlyCollection<RelationshipType>? relationshipTypes, TraversalDirection direction, CancellationToken ct)
    {
        maxDepth = Math.Clamp(maxDepth, 1, MaxImpactDepth);
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);

        var reachable = await GetReachableNodeIdsAsync(connection, nodeId, maxDepth, relationshipTypes, direction, ct);
        reachable.Remove(nodeId);

        var affectedNodes = reachable.Count == 0
            ? []
            : await GetNodesByIdsAsync(connection, reachable.Keys, ct);

        var affectedByType = affectedNodes
            .GroupBy(n => n.NodeType)
            .ToDictionary(g => g.Key, g => g.Count());

        var samplePaths = await GetPathsAsync(connection, nodeId, maxDepth, relationshipTypes, direction, targetNodeId: null, limit: SamplePathLimit, ct);

        return new ImpactResultDto
        {
            RootNodeId = nodeId,
            AffectedNodes = affectedNodes,
            SamplePaths = samplePaths,
            AffectedByType = affectedByType,
        };
    }

    public async Task<SubgraphDto> GetNeighborhoodAsync(GetNeighborhoodRequest request, CancellationToken ct = default)
    {
        var depth = Math.Clamp(request.Depth, 1, MaxNeighborhoodDepth);
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);

        var forward = await GetReachableNodeIdsAsync(connection, request.SeedNodeId, depth, request.RelationshipTypes, TraversalDirection.Forward, ct);
        var reverse = await GetReachableNodeIdsAsync(connection, request.SeedNodeId, depth, request.RelationshipTypes, TraversalDirection.Reverse, ct);

        var merged = new Dictionary<string, int>(forward);
        foreach (var (id, d) in reverse)
        {
            if (!merged.TryGetValue(id, out var existing) || d < existing)
            {
                merged[id] = d;
            }
        }

        merged[request.SeedNodeId] = 0;

        var candidateNodes = await GetNodesByIdsAsync(connection, merged.Keys, ct);

        IEnumerable<NodeDto> filtered = candidateNodes;
        if (request.NodeTypes is { Count: > 0 })
        {
            var typeSet = request.NodeTypes.ToHashSet();
            filtered = filtered.Where(n => typeSet.Contains(n.NodeType));
        }

        if (!request.IncludeExternal)
        {
            filtered = filtered.Where(n => !n.IsExternal);
        }

        var ordered = filtered
            .OrderBy(n => merged[n.NodeId])
            .ThenBy(n => n.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var truncated = ordered.Count > request.MaxNodes;
        var finalNodes = ordered.Take(request.MaxNodes).ToList();

        var edges = await GetEdgesAmongAsync(connection, finalNodes.Select(n => n.NodeId).ToList(), request.RelationshipTypes, ct);

        return new SubgraphDto { Nodes = finalNodes, Edges = edges, Truncated = truncated };
    }

    public async Task<SubgraphDto> GetSubgraphAsync(GetSubgraphRequest request, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);

        var effectivePageSize = Math.Max(1, Math.Min(request.PageSize, request.MaxNodes));
        var offset = request.Page * request.PageSize;

        var projectIdsNull = request.ProjectIds is null or { Count: 0 } ? 1 : 0;
        var nodeTypesNull = request.NodeTypes is null or { Count: 0 } ? 1 : 0;

        var sql = $"""
            SELECT {NodeColumns} FROM nodes
            WHERE is_deleted = 0
              AND (@ProjectIdsNull = 1 OR project_id IN @ProjectIds)
              AND (@NodeTypesNull = 1 OR node_type IN @NodeTypes)
            ORDER BY full_name
            LIMIT @Limit OFFSET @Offset
            """;

        var nodes = (await connection.QueryAsync<NodeDto>(sql, new
        {
            ProjectIdsNull = projectIdsNull,
            ProjectIds = request.ProjectIds?.ToArray() ?? [],
            NodeTypesNull = nodeTypesNull,
            NodeTypes = request.NodeTypes?.Select(t => t.ToString()).ToArray() ?? [],
            Limit = effectivePageSize + 1,
            Offset = offset,
        })).ToList();

        var truncated = nodes.Count > effectivePageSize;
        var finalNodes = nodes.Take(effectivePageSize).ToList();

        // Edges are reconstructed only among the nodes actually returned on this page — an edge
        // crossing a page boundary won't appear until both endpoints happen to be paged in together.
        // Acceptable for interactive rendering (the doc's stated use case), not a complete edge audit.
        var edges = await GetEdgesAmongAsync(connection, finalNodes.Select(n => n.NodeId).ToList(), request.RelationshipTypes, ct);

        return new SubgraphDto { Nodes = finalNodes, Edges = edges, Truncated = truncated };
    }

    public async Task<IReadOnlyList<PathDto>> FindPathsAsync(string sourceNodeId, string targetNodeId, int maxDepth = 8, CancellationToken ct = default)
    {
        maxDepth = Math.Clamp(maxDepth, 1, MaxPathDepth);
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        return await GetPathsAsync(connection, sourceNodeId, maxDepth, relationshipTypes: null, TraversalDirection.Forward, targetNodeId, MaxPathResults, ct);
    }

    /// <summary>Node ids reachable from startNodeId within maxDepth hops, following edges in the given
    /// direction, mapped to the minimum depth at which each was reached. No cycle collapsing mid-walk
    /// (see the type-level doc comment) — bounded by the maxDepth cap instead.</summary>
    private static async Task<Dictionary<string, int>> GetReachableNodeIdsAsync(
        System.Data.IDbConnection connection,
        string startNodeId,
        int maxDepth,
        IReadOnlyCollection<RelationshipType>? relationshipTypes,
        TraversalDirection direction,
        CancellationToken ct)
    {
        var (fromColumn, toColumn) = DirectionColumns(direction);
        var relTypesNull = relationshipTypes is null or { Count: 0 } ? 1 : 0;
        var relTypes = relationshipTypes?.Select(r => r.ToString()).ToArray() ?? [];

        var sql = $"""
            WITH RECURSIVE reach(node_id, depth) AS (
                SELECT @StartNodeId, 0
                UNION
                SELECT e.{toColumn}, r.depth + 1
                FROM edges e
                JOIN reach r ON e.{fromColumn} = r.node_id
                WHERE r.depth < @MaxDepth
                  AND e.is_deleted = 0
                  AND (@RelTypesNull = 1 OR e.relationship_type IN @RelTypes)
            )
            SELECT node_id AS NodeId, MIN(depth) AS Depth
            FROM reach
            GROUP BY node_id
            """;

        var rows = await connection.QueryAsync<ReachRow>(sql, new
        {
            StartNodeId = startNodeId,
            MaxDepth = maxDepth,
            RelTypesNull = relTypesNull,
            RelTypes = relTypes,
        });

        return rows.ToDictionary(r => r.NodeId, r => r.Depth);
    }

    /// <summary>Simple (non-repeating) paths from startNodeId, up to maxDepth hops, optionally
    /// filtered to only those reaching targetNodeId — the one traversal here where cycle prevention
    /// (via instr() against the accumulated pipe-delimited path) is a correctness requirement, not
    /// just a performance one. `limit` (with no ORDER BY) lets SQLite stop expanding the recursive
    /// CTE as soon as enough rows exist, so a small SamplePaths/FindPaths request stays cheap even on
    /// a large graph.</summary>
    private static async Task<List<PathDto>> GetPathsAsync(
        System.Data.IDbConnection connection,
        string startNodeId,
        int maxDepth,
        IReadOnlyCollection<RelationshipType>? relationshipTypes,
        TraversalDirection direction,
        string? targetNodeId,
        int limit,
        CancellationToken ct)
    {
        var (fromColumn, toColumn) = DirectionColumns(direction);
        var relTypesNull = relationshipTypes is null or { Count: 0 } ? 1 : 0;
        var relTypes = relationshipTypes?.Select(r => r.ToString()).ToArray() ?? [];
        var targetFilter = targetNodeId is not null ? "WHERE node_id = @TargetNodeId" : "WHERE depth > 0";

        var sql = $"""
            WITH RECURSIVE walk(node_id, depth, path_nodes, path_edges) AS (
                SELECT @StartNodeId, 0, @StartNodeId, ''
                UNION ALL
                SELECT e.{toColumn}, w.depth + 1,
                       w.path_nodes || '|' || e.{toColumn},
                       CASE WHEN w.path_edges = '' THEN e.edge_id ELSE w.path_edges || '|' || e.edge_id END
                FROM edges e
                JOIN walk w ON e.{fromColumn} = w.node_id
                WHERE w.depth < @MaxDepth
                  AND e.is_deleted = 0
                  AND (@RelTypesNull = 1 OR e.relationship_type IN @RelTypes)
                  AND instr('|' || w.path_nodes || '|', '|' || e.{toColumn} || '|') = 0
            )
            SELECT path_nodes AS PathNodes, path_edges AS PathEdges
            FROM walk
            {targetFilter}
            LIMIT @Limit
            """;

        var rows = await connection.QueryAsync<PathRow>(sql, new
        {
            StartNodeId = startNodeId,
            MaxDepth = maxDepth,
            RelTypesNull = relTypesNull,
            RelTypes = relTypes,
            TargetNodeId = targetNodeId,
            Limit = limit,
        });

        return rows.Select(r => new PathDto
        {
            NodeIds = r.PathNodes.Split('|'),
            EdgeIds = string.IsNullOrEmpty(r.PathEdges) ? [] : r.PathEdges.Split('|'),
        }).ToList();
    }

    private static async Task<List<NodeDto>> GetNodesByIdsAsync(System.Data.IDbConnection connection, IEnumerable<string> nodeIds, CancellationToken ct)
    {
        var ids = nodeIds.ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var sql = $"SELECT {NodeColumns} FROM nodes WHERE node_id IN @NodeIds AND is_deleted = 0";
        var results = await connection.QueryAsync<NodeDto>(sql, new { NodeIds = ids });
        return results.ToList();
    }

    private static async Task<List<EdgeDto>> GetEdgesAmongAsync(
        System.Data.IDbConnection connection, IReadOnlyCollection<string> nodeIds, IReadOnlyCollection<RelationshipType>? relationshipTypes, CancellationToken ct)
    {
        if (nodeIds.Count == 0)
        {
            return [];
        }

        var relTypesNull = relationshipTypes is null or { Count: 0 } ? 1 : 0;
        var relTypes = relationshipTypes?.Select(r => r.ToString()).ToArray() ?? [];

        var sql = """
            SELECT edge_id AS EdgeId, repo_id AS RepoId, source_id AS SourceId, target_id AS TargetId,
                   relationship_type AS RelationshipType, metadata_json AS Metadata
            FROM edges
            WHERE is_deleted = 0
              AND source_id IN @NodeIds AND target_id IN @NodeIds
              AND (@RelTypesNull = 1 OR relationship_type IN @RelTypes)
            """;

        var results = await connection.QueryAsync<EdgeDto>(sql, new
        {
            NodeIds = nodeIds.ToArray(),
            RelTypesNull = relTypesNull,
            RelTypes = relTypes,
        });

        return results.ToList();
    }

    private static (string FromColumn, string ToColumn) DirectionColumns(TraversalDirection direction)
        => direction == TraversalDirection.Forward ? ("source_id", "target_id") : ("target_id", "source_id");

    private sealed record ReachRow
    {
        public string NodeId { get; init; } = "";
        public int Depth { get; init; }
    }

    private sealed record PathRow
    {
        public string PathNodes { get; init; } = "";
        public string PathEdges { get; init; } = "";
    }
}
