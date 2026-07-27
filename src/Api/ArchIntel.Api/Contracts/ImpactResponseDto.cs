namespace ArchIntel.Api.Contracts;

/// <summary>`GET /impact` (05-rest-api.md Section 4.5, Phase 3: transitive depth + risk
/// annotation). `RiskLevel` is a simple depth-based heuristic (Low/Medium/High), not an LLM
/// judgment — real risk scoring is a Section 10 open question (Architecture quality scoring)
/// this document doesn't resolve either.</summary>
public sealed record AffectedNodeDto(string Id, string Kind, string Name, string Relation, int Depth, string RiskLevel);

public sealed record ImpactSummaryDto(int TotalAffected, IReadOnlyDictionary<string, int> ByKind);

public sealed record ImpactResponseDto(
    string TargetId,
    string TargetName,
    IReadOnlyList<AffectedNodeDto> Affected,
    ImpactSummaryDto Summary);
