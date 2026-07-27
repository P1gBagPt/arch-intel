using ArchIntel.Api.Contracts;
using ArchIntel.Api.Mapping;
using ArchIntel.Api.Pagination;
using ArchIntel.Api.Problems;
using ArchIntel.GraphStore.Contracts;

namespace ArchIntel.Api.Endpoints;

/// <summary>`GET /projects` (05-rest-api.md Section 4.1). Phase 2 adds cursor pagination.</summary>
public static class ProjectsEndpoints
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 500;

    public static IEndpointRouteBuilder MapProjectsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/projects", async (IGraphReader reader, string? cursor, int? limit, CancellationToken ct) =>
        {
            if (!CursorPagination.TryDecode(cursor, out var offset))
            {
                return ProblemTypes.InvalidQuery("Malformed cursor.");
            }

            var effectiveLimit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
            var projects = await reader.ListProjectsAsync(ct: ct);
            var page = projects.Skip(offset).Take(effectiveLimit).Select(p => p.ToDto()).ToList();
            var hasNextPage = offset + effectiveLimit < projects.Count;

            var pageInfo = new PageInfo(
                effectiveLimit,
                projects.Count,
                hasNextPage,
                hasNextPage ? CursorPagination.Encode(offset + effectiveLimit) : null);

            return Results.Ok(new ApiEnvelope<IReadOnlyList<ProjectSummaryDto>>(page, pageInfo));
        })
        .WithName("GetProjects")
        .WithTags("Projects")
        .Produces<ApiEnvelope<IReadOnlyList<ProjectSummaryDto>>>()
        .ProducesValidationProblem();

        return app;
    }
}
