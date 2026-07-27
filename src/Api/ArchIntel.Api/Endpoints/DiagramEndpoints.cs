using ArchIntel.Api.Contracts;
using ArchIntel.Api.Problems;
using ArchIntel.Api.Rendering;
using ArchIntel.Api.Resolution;
using ArchIntel.GraphStore.Contracts;

namespace ArchIntel.Api.Endpoints;

/// <summary>`POST /diagram` (05-rest-api.md Section 4.7). Shares scope/depth/kinds resolution with
/// `GET /graph` via GraphScopeResolver, then renders Mermaid over whatever subgraph comes back.</summary>
public static class DiagramEndpoints
{
    private const int DefaultDepth = 2;

    public static IEndpointRouteBuilder MapDiagramEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/diagram", async (string repoId, DiagramRequestDto request, IGraphReader reader, CancellationToken ct) =>
        {
            if (!string.Equals(request.Format, "mermaid", StringComparison.OrdinalIgnoreCase))
            {
                return ProblemTypes.InvalidQuery($"Unsupported format '{request.Format}'. Only 'mermaid' is implemented.");
            }

            if (!GraphScopeResolver.TryParseKinds(request.Kinds is null ? null : string.Join(',', request.Kinds), out var kinds, out var kindsError))
            {
                return ProblemTypes.InvalidQuery(kindsError!);
            }

            var (subgraph, error) = await GraphScopeResolver.ResolveAsync(
                reader, repoId, request.Scope, request.Depth <= 0 ? DefaultDepth : request.Depth, kinds, full: request.Scope is null, "/diagram", ct);
            if (error is not null)
            {
                return error;
            }

            var content = MermaidDiagramRenderer.Render(subgraph!);
            return Results.Ok(new ApiEnvelope<DiagramResponseDto>(new DiagramResponseDto("mermaid", content)));
        })
        .WithName("PostDiagram")
        .WithTags("Diagram")
        // AI/cost-sensitive-adjacent per 05-rest-api.md Section 6.3's policy table (grouped with
        // the Planning endpoints below RequireRepoOwner but above plain Viewer).
        .RequireAuthorization("RequireRepoMaintainer")
        .RequireRateLimiting("ai-operations")
        .Produces<ApiEnvelope<DiagramResponseDto>>()
        .ProducesValidationProblem()
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
