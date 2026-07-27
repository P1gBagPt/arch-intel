namespace ArchIntel.Api.Jobs;

/// <summary>Async job pattern for long-running AI operations (05-rest-api.md Section 3.6).
/// Phase 3's in-memory store loses in-flight jobs on restart — accepted per the doc's own Section
/// 10 risk note; a durable store is a Phase 4 concern if multi-instance deployment ever happens.</summary>
public enum JobStatus { Pending, Running, Completed, Failed }

/// <summary>Mirrors 05-rest-api.md Section 3.4's Problem Details shape, trimmed to what a job
/// failure needs to report (title + status) rather than the full RFC 9457 object.</summary>
public sealed record JobProblemSummary(string Title, int Status);

public sealed record JobRecord
{
    public required string JobId { get; init; }
    public required JobStatus Status { get; init; }
    public int? ProgressPercent { get; init; }
    public object? Result { get; init; }
    public JobProblemSummary? Problem { get; init; }
}
