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
}

public sealed record EdgeWithNodeDto
{
    public required EdgeDto Edge { get; init; }

    /// <summary>The target (for GetDependencies) or source (for GetCallers).</summary>
    public required NodeDto OtherNode { get; init; }
}
