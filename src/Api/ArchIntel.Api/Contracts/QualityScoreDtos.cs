namespace ArchIntel.Api.Contracts;

/// <summary>`GET /quality-score` (05-rest-api.md Section 4.12) — marked in the plan itself as
/// "proposed/open... this document's best guess, not a confirmed design, needs product input
/// before implementation." The factor list (coupling, circular dependencies, test-coverage proxy)
/// and their weights/thresholds are exactly that: a documented, overridable heuristic, not a
/// validated methodology.</summary>
public sealed record QualityFactorDto(string Name, int Score, double Weight);

public sealed record QualityScoreDto(int OverallScore, string Band, IReadOnlyList<QualityFactorDto> Factors);
