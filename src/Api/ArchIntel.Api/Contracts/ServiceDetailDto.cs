namespace ArchIntel.Api.Contracts;

/// <summary>`GET /services/{id}` (05-rest-api.md Section 4.3, Phase 2).</summary>
public sealed record ServiceDetailDto(
    string Id,
    string Name,
    string Kind,
    string ProjectId,
    IReadOnlyList<NodeRefDto> Dependencies,
    IReadOnlyList<NodeRefDto> Callers,
    IReadOnlyList<NodeRefDto> Implements,
    IReadOnlyList<NodeRefDto> Tests);
