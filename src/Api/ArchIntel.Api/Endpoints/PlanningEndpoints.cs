using ArchIntel.Api.Contracts;
using ArchIntel.Api.Jobs;
using ArchIntel.Api.Planning;
using ArchIntel.Api.Problems;
using ArchIntel.Api.Realtime;

namespace ArchIntel.Api.Endpoints;

/// <summary>`POST /implementation-plan` and `POST /architecture-analysis` (05-rest-api.md Sections
/// 4.8/4.9) — both delegate to the shared IPlanningService and follow the async job pattern from
/// Section 3.6: return 202 + jobId immediately, do the work in the background, and notify
/// completion over SignalR so a dashboard tab that isn't polling still finds out.</summary>
public static class PlanningEndpoints
{
    public static IEndpointRouteBuilder MapPlanningEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/implementation-plan", (
            string repoId, ImplementationPlanRequest request, JobStore jobStore, IPlanningService planningService, IArchitectureChangeNotifier notifier) =>
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                return ProblemTypes.InvalidQuery("'prompt' is required.");
            }

            var job = jobStore.Create();
            var scopedRequest = request with { RepoId = repoId };

            // Deliberately CancellationToken.None, not the request's — this work must outlive the
            // 202 response that ends the HTTP request's lifetime.
            _ = RunJobAsync(jobStore, notifier, repoId, job.JobId,
                () => planningService.GeneratePlanAsync(scopedRequest, CancellationToken.None));

            return Results.Accepted($"/api/v1/repos/{repoId}/jobs/{job.JobId}", new ApiEnvelope<JobAcceptedDto>(new JobAcceptedDto(job.JobId, job.Status.ToString())));
        })
        .WithName("PostImplementationPlan")
        .WithTags("Planning")
        .RequireAuthorization("RequireRepoMaintainer")
        .RequireRateLimiting("ai-operations")
        .Produces<ApiEnvelope<JobAcceptedDto>>(StatusCodes.Status202Accepted)
        .ProducesValidationProblem();

        app.MapPost("/architecture-analysis", (
            string repoId, ArchitectureAnalysisRequest request, JobStore jobStore, IPlanningService planningService, IArchitectureChangeNotifier notifier) =>
        {
            if (string.IsNullOrWhiteSpace(request.Question))
            {
                return ProblemTypes.InvalidQuery("'question' is required.");
            }

            var job = jobStore.Create();

            _ = RunJobAsync(jobStore, notifier, repoId, job.JobId,
                () => planningService.AnalyzeAsync(request, CancellationToken.None));

            return Results.Accepted($"/api/v1/repos/{repoId}/jobs/{job.JobId}", new ApiEnvelope<JobAcceptedDto>(new JobAcceptedDto(job.JobId, job.Status.ToString())));
        })
        .WithName("PostArchitectureAnalysis")
        .WithTags("Planning")
        .RequireAuthorization("RequireRepoMaintainer")
        .RequireRateLimiting("ai-operations")
        .Produces<ApiEnvelope<JobAcceptedDto>>(StatusCodes.Status202Accepted)
        .ProducesValidationProblem();

        return app;
    }

    private static async Task RunJobAsync<TResult>(JobStore jobStore, IArchitectureChangeNotifier notifier, string repoId, string jobId, Func<Task<TResult>> work)
    {
        jobStore.Update(new JobRecord { JobId = jobId, Status = JobStatus.Running });

        try
        {
            var result = await work();
            jobStore.Update(new JobRecord { JobId = jobId, Status = JobStatus.Completed, Result = result });
            await notifier.JobCompletedAsync(repoId, new JobCompletedEvent(jobId, JobStatus.Completed.ToString()));
        }
        catch (Exception ex)
        {
            var problem = ProblemTypes.PlanningServiceUnavailable() with { Title = ex.Message };
            jobStore.Update(new JobRecord { JobId = jobId, Status = JobStatus.Failed, Problem = problem });
            await notifier.JobFailedAsync(repoId, new JobFailedEvent(jobId, JobStatus.Failed.ToString(), problem));
        }
    }
}
