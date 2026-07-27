using ArchIntel.Api.Analysis;
using ArchIntel.Api.Contracts;
using ArchIntel.Api.Mapping;
using ArchIntel.Api.Resolution;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;

namespace ArchIntel.Api.Endpoints;

/// <summary>`GET /quality-score` (05-rest-api.md Section 4.12, "proposed" per the plan itself).</summary>
public static class QualityScoreEndpoints
{
    public static IEndpointRouteBuilder MapQualityScoreEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/quality-score", async (string repoId, IGraphReader reader, CancellationToken ct) =>
        {
            var projects = await reader.ListProjectsAsync(repoId, ct);
            var testClassCount = 0;
            var serviceCount = 0;
            foreach (var project in projects)
            {
                var nodes = await reader.GetNodesByProjectAsync(project.ProjectId, nodeType: null, ct: ct);
                testClassCount += nodes.Count(n => n.NodeType == NodeType.TestClass);
                serviceCount += nodes.Count(n => ServiceNodeTypes.Kinds.Contains(n.NodeType));
            }

            var graph = await reader.GetSubgraphAsync(new GetSubgraphRequest
            {
                MaxNodes = GraphScopeResolver.UnscopedMaxNodes,
                PageSize = GraphScopeResolver.UnscopedMaxNodes,
            }, ct);
            var coupling = GraphMetricsComputer.ComputeProjectCoupling(graph);
            var cycleCount = GraphMetricsComputer.FindProjectCycles(graph).Count;

            var dto = QualityScoreComputer.Compute(coupling, cycleCount, testClassCount, serviceCount);
            return Results.Ok(new ApiEnvelope<QualityScoreDto>(dto));
        })
        .WithName("GetQualityScore")
        .WithTags("Metrics")
        .RequireAuthorization("RequireRepoViewer")
        .Produces<ApiEnvelope<QualityScoreDto>>();

        return app;
    }
}
