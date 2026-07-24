using ArchIntel.GraphStore.Contracts.Enums;

namespace ArchIntel.GraphStore.Contracts;

/// <summary>
/// Phase 1 subset of the Reader contract. Consumed by the CLI and (later) the MCP server / REST API.
/// No SQL or storage detail leaks past this interface.
/// </summary>
public interface IGraphReader
{
    Task<NodeDto?> GetNodeAsync(string nodeId, CancellationToken ct = default);

    Task<IReadOnlyList<NodeDto>> FindByNameAsync(string name, NodeType? nodeType = null, bool exactMatch = false, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectDto>> ListProjectsAsync(string repoId = "default", CancellationToken ct = default);

    Task<IReadOnlyList<NodeDto>> GetNodesByProjectAsync(string projectId, NodeType? nodeType = null, CancellationToken ct = default);

    /// <summary>Direct (1-hop) outgoing dependencies of a node, optionally filtered by relationship type.</summary>
    Task<IReadOnlyList<EdgeWithNodeDto>> GetDependenciesAsync(string nodeId, RelationshipType? relationshipType = null, CancellationToken ct = default);

    /// <summary>Direct (1-hop) incoming edges — i.e. who depends on / calls this node.</summary>
    Task<IReadOnlyList<EdgeWithNodeDto>> GetCallersAsync(string nodeId, RelationshipType? relationshipType = null, CancellationToken ct = default);

    /// <summary>The most recently completed scan for a repo, if any — lets consumers (e.g. the MCP
    /// Server's tool responses) stamp results with a data-freshness signal.</summary>
    Task<ScanMetadataDto?> GetLatestScanMetadataAsync(string repoId = "default", CancellationToken ct = default);

    // ---- Phase 2: subgraph extraction & rendering support ----

    /// <summary>Transitive impact set: everything reachable FROM nodeId within maxDepth hops, following the given relationship types (default: all).</summary>
    Task<ImpactResultDto> GetImpactAsync(string nodeId, int maxDepth = 10, IReadOnlyCollection<RelationshipType>? relationshipTypes = null, CancellationToken ct = default);

    /// <summary>Transitive dependents: everything that can reach nodeId within maxDepth hops (reverse traversal). Used for "what breaks if I change this".</summary>
    Task<ImpactResultDto> GetTransitiveDependentsAsync(string nodeId, int maxDepth = 10, IReadOnlyCollection<RelationshipType>? relationshipTypes = null, CancellationToken ct = default);

    /// <summary>Extracts a renderable subgraph (nodes + edges) around a seed node, for Cytoscape/Sigma/React Flow consumption.</summary>
    Task<SubgraphDto> GetNeighborhoodAsync(GetNeighborhoodRequest request, CancellationToken ct = default);

    /// <summary>Extracts a full or filtered subgraph for a project/set of projects (e.g. "show me the whole Business layer").</summary>
    Task<SubgraphDto> GetSubgraphAsync(GetSubgraphRequest request, CancellationToken ct = default);

    /// <summary>Finds all simple paths between two nodes, up to maxDepth — used for "how does A reach B" queries and diagram generation.</summary>
    Task<IReadOnlyList<PathDto>> FindPathsAsync(string sourceNodeId, string targetNodeId, int maxDepth = 8, CancellationToken ct = default);
}

public sealed record ScanMetadataDto
{
    public required long ScanRunId { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
}

public sealed record EdgeWithNodeDto
{
    public required EdgeDto Edge { get; init; }

    /// <summary>The target (for GetDependencies) or source (for GetCallers).</summary>
    public required NodeDto OtherNode { get; init; }
}
