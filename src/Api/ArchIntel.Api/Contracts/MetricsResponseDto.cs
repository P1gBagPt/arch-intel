namespace ArchIntel.Api.Contracts;

/// <summary>`GET /metrics` (05-rest-api.md Section 4.6, Phase 2 basic totals). IGraphReader has no
/// single aggregate query, so this is composed from ListProjectsAsync + GetNodesByProjectAsync
/// across every project — acceptable at local-dev scale; coupling/circular-dependency metrics are
/// a Phase 3 addition.</summary>
public sealed record MetricsResponseDto(
    int TotalProjects,
    int TotalClasses,
    int TotalInterfaces,
    int TotalServices,
    DateTimeOffset GeneratedAtUtc);
