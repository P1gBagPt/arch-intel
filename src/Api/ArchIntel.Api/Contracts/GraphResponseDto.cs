namespace ArchIntel.Api.Contracts;

public sealed record GraphNodeDto(string Id, string Kind, string Name);

public sealed record GraphEdgeDto(string FromId, string ToId, string Type);

public sealed record GraphResponseDto(
    IReadOnlyList<GraphNodeDto> Nodes,
    IReadOnlyList<GraphEdgeDto> Edges,
    bool Truncated);
