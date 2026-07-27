using ArchIntel.Api.Jobs;

namespace ArchIntel.Api.Contracts;

/// <summary>`POST /implementation-plan` / `POST /architecture-analysis` 202 response
/// (05-rest-api.md Section 4.8/4.9).</summary>
public sealed record JobAcceptedDto(string JobId, string Status);

/// <summary>`GET /jobs/{jobId}` (05-rest-api.md Section 4.10). `Result` is whichever of
/// ImplementationPlanResult/ArchitectureAnalysisResult the job produced.</summary>
public sealed record JobStatusResponseDto(string JobId, string Status, int? ProgressPercent, object? Result, JobProblemSummary? Problem);
