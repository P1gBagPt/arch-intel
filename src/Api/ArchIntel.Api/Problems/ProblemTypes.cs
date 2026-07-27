using ArchIntel.Api.Jobs;

namespace ArchIntel.Api.Problems;

/// <summary>Standard problem types (05-rest-api.md Section 3.4), kept as one small helper rather
/// than pulling in FluentValidation for the handful of query-param checks Phase 2 needs.</summary>
public static class ProblemTypes
{
    public static IResult NodeNotFound(string nodeId, string instance) => Results.Problem(
        type: "https://arch-intel.dev/problems/node-not-found",
        title: "Graph node not found",
        statusCode: StatusCodes.Status404NotFound,
        detail: $"No node with id '{nodeId}' exists in the current graph snapshot.",
        instance: instance);

    public static IResult InvalidQuery(string detail) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["query"] = [detail] });

    public static IResult JobNotFound(string jobId, string instance) => Results.Problem(
        type: "https://arch-intel.dev/problems/job-not-found",
        title: "Job not found",
        statusCode: StatusCodes.Status404NotFound,
        detail: $"No job with id '{jobId}' exists (in-memory job store — jobs don't survive an API restart).",
        instance: instance);

    public static IResult InvitationNotFound(string invitationId, string instance) => Results.Problem(
        type: "https://arch-intel.dev/problems/invitation-not-found",
        title: "Invitation not found",
        statusCode: StatusCodes.Status404NotFound,
        detail: $"No pending invitation with id '{invitationId}' exists for this repo (in-memory store — invitations don't survive an API restart).",
        instance: instance);

    /// <summary>Phase 3's `.../problems/planning-service-unavailable` (05-rest-api.md Section 3.4)
    /// — reused whenever a background job's IPlanningService call throws, so callers can tell
    /// "your input was bad" apart from "try again later" without parsing free-text details.</summary>
    public static JobProblemSummary PlanningServiceUnavailable() => new("Planning service unavailable", StatusCodes.Status503ServiceUnavailable);

    public static IResult SnapshotNotFound(string snapshotId, string instance) => Results.Problem(
        type: "https://arch-intel.dev/problems/snapshot-not-found",
        title: "Snapshot not found",
        statusCode: StatusCodes.Status404NotFound,
        detail: $"No snapshot with id '{snapshotId}' — only the current scan's snapshot is available (see Section 10: historical snapshots aren't retained yet).",
        instance: instance);

    public static IResult SnapshotHistoryUnavailable(string instance) => Results.Problem(
        type: "https://arch-intel.dev/problems/snapshot-history-unavailable",
        title: "Snapshot history not available",
        statusCode: StatusCodes.Status501NotImplemented,
        detail: "Diffing between snapshots requires the Graph Store to retain historical scan state, which it doesn't yet (a full scan replaces prior data rather than keeping it). This is a Graph Store schema/writer gap (02-graph-store.md), not something the REST API can resolve unilaterally.",
        instance: instance);
}
