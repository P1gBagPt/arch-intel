using ArchIntel.GraphStore.Contracts.Enums;

namespace ArchIntel.GraphStore.Contracts;

/// <summary>Phase 2 subgraph/impact traversal DTOs (02-graph-store.md Section 5.1).</summary>
public sealed record ImpactResultDto
{
    public required string RootNodeId { get; init; }
    public required IReadOnlyList<NodeDto> AffectedNodes { get; init; }
    public required IReadOnlyList<PathDto> SamplePaths { get; init; }
    public required IReadOnlyDictionary<NodeType, int> AffectedByType { get; init; }
}

public sealed record GetNeighborhoodRequest
{
    public required string SeedNodeId { get; init; }
    public int Depth { get; init; } = 1;
    public IReadOnlyCollection<RelationshipType>? RelationshipTypes { get; init; }
    public IReadOnlyCollection<NodeType>? NodeTypes { get; init; }
    public bool IncludeExternal { get; init; } = true;
    public int MaxNodes { get; init; } = 500;
}

public sealed record GetSubgraphRequest
{
    public IReadOnlyCollection<string>? ProjectIds { get; init; }
    public IReadOnlyCollection<NodeType>? NodeTypes { get; init; }
    public IReadOnlyCollection<RelationshipType>? RelationshipTypes { get; init; }
    public int MaxNodes { get; init; } = 2000;
    public int Page { get; init; } = 0;
    public int PageSize { get; init; } = 500;
}

public sealed record SubgraphDto
{
    public required IReadOnlyList<NodeDto> Nodes { get; init; }
    public required IReadOnlyList<EdgeDto> Edges { get; init; }
    public bool Truncated { get; init; }
}

public sealed record PathDto
{
    public required IReadOnlyList<string> NodeIds { get; init; }
    public required IReadOnlyList<string> EdgeIds { get; init; }
}
