using ArchIntel.Api.Contracts;
using ArchIntel.Api.Mapping;
using ArchIntel.Api.Problems;
using ArchIntel.Api.Resolution;
using ArchIntel.GraphStore.Contracts;

namespace ArchIntel.Api.Endpoints;

/// <summary>`GET /graph` (05-rest-api.md Section 4.4). Phase 2 adds `scope`/`depth`/`kinds`
/// filtering on top of Phase 1's unfiltered whole-graph response (still available via
/// `full=true` or simply omitting `scope`).</summary>
public static class GraphEndpoints
{
    private const int DefaultDepth = 2;

    public static IEndpointRouteBuilder MapGraphEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/graph", async (string repoId, IGraphReader reader, string? scope, int? depth, string? kinds, bool? full, CancellationToken ct) =>
        {
            if (!GraphScopeResolver.TryParseKinds(kinds, out var kindFilter, out var kindsError))
            {
                return ProblemTypes.InvalidQuery(kindsError!);
            }

            var (subgraph, error) = await GraphScopeResolver.ResolveAsync(
                reader, repoId, scope, depth ?? DefaultDepth, kindFilter, full ?? false, $"/graph?scope={scope}", ct);
            if (error is not null)
            {
                return error;
            }

            var dto = new GraphResponseDto(
                subgraph!.Nodes.Select(n => n.ToGraphNodeDto()).ToList(),
                subgraph.Edges.Select(e => e.ToGraphEdgeDto()).ToList(),
                subgraph.Truncated);

            return Results.Ok(new ApiEnvelope<GraphResponseDto>(dto));
        })
        .WithName("GetGraph")
        .WithTags("Graph")
        .RequireAuthorization("RequireRepoViewer")
        .Produces<ApiEnvelope<GraphResponseDto>>()
        .ProducesValidationProblem()
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
