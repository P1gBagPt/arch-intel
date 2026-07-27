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

    /// <summary>Phase 3's `.../problems/planning-service-unavailable` (05-rest-api.md Section 3.4)
    /// — reused whenever a background job's IPlanningService call throws, so callers can tell
    /// "your input was bad" apart from "try again later" without parsing free-text details.</summary>
    public static JobProblemSummary PlanningServiceUnavailable() => new("Planning service unavailable", StatusCodes.Status503ServiceUnavailable);
}
