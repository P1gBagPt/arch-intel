namespace ArchIntel.Api.Contracts;

/// <summary>`GET /metrics/coupling` (05-rest-api.md Section 4.6, Phase 3). `Band` is a
/// server-computed Green/Yellow/Red classification so the dashboard doesn't reimplement
/// thresholding — thresholds configurable via `Metrics:CouplingBands:Green`/`Yellow`.</summary>
public sealed record CouplingMetricDto(string ProjectId, string ProjectName, int AfferentCoupling, int EfferentCoupling, double Instability, string Band);

/// <summary>`GET /metrics/circular-dependencies`.</summary>
public sealed record CircularDependencyDto(IReadOnlyList<string> Cycle, int Length);
