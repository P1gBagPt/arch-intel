using ArchIntel.Api.Contracts;
using ArchIntel.Api.Jobs;
using ArchIntel.Api.Problems;

namespace ArchIntel.Api.Endpoints;

/// <summary>`GET /jobs/{jobId}` (05-rest-api.md Section 4.10) — supporting endpoint for the async
/// job pattern behind `POST /implementation-plan` and `POST /architecture-analysis`.</summary>
public static class JobsEndpoints
{
    public static IEndpointRouteBuilder MapJobsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/jobs/{jobId}", (string jobId, JobStore jobStore) =>
        {
            var job = jobStore.Get(jobId);
            if (job is null)
            {
                return ProblemTypes.JobNotFound(jobId, $"/jobs/{jobId}");
            }

            var dto = new JobStatusResponseDto(job.JobId, job.Status.ToString(), job.ProgressPercent, job.Result, job.Problem);
            return Results.Ok(new ApiEnvelope<JobStatusResponseDto>(dto));
        })
        .WithName("GetJob")
        .WithTags("Planning")
        .Produces<ApiEnvelope<JobStatusResponseDto>>()
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
