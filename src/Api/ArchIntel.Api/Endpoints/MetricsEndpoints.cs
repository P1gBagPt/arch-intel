using ArchIntel.Api.Analysis;
using ArchIntel.Api.Contracts;
using ArchIntel.Api.Mapping;
using ArchIntel.Api.Resolution;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using Microsoft.Extensions.Configuration;

namespace ArchIntel.Api.Endpoints;

/// <summary>`GET /metrics` (05-rest-api.md Section 4.6). Phase 2 covers basic totals; Phase 3 adds
/// `GET /metrics/coupling` and `GET /metrics/circular-dependencies`.</summary>
public static class MetricsEndpoints
{
    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/metrics", async (IGraphReader reader, TimeProvider time, CancellationToken ct) =>
        {
            var projects = await reader.ListProjectsAsync(ct: ct);

            var totalClasses = 0;
            var totalInterfaces = 0;
            var totalServices = 0;
            foreach (var project in projects)
            {
                var nodes = await reader.GetNodesByProjectAsync(project.ProjectId, nodeType: null, ct: ct);
                totalClasses += nodes.Count(n => n.NodeType == NodeType.Class);
                totalInterfaces += nodes.Count(n => n.NodeType == NodeType.Interface);
                totalServices += nodes.Count(n => ServiceNodeTypes.Kinds.Contains(n.NodeType));
            }

            var dto = new MetricsResponseDto(projects.Count, totalClasses, totalInterfaces, totalServices, time.GetUtcNow());
            return Results.Ok(new ApiEnvelope<MetricsResponseDto>(dto));
        })
        .WithName("GetMetrics")
        .WithTags("Metrics")
        .Produces<ApiEnvelope<MetricsResponseDto>>();

        app.MapGet("/metrics/coupling", async (IGraphReader reader, IConfiguration config, CancellationToken ct) =>
        {
            var greenMax = config.GetValue("Metrics:CouplingBands:Green", 0.3);
            var yellowMax = config.GetValue("Metrics:CouplingBands:Yellow", 0.7);

            var projects = await reader.ListProjectsAsync(ct: ct);
            var graph = await reader.GetSubgraphAsync(new GetSubgraphRequest
            {
                MaxNodes = GraphScopeResolver.UnscopedMaxNodes,
                PageSize = GraphScopeResolver.UnscopedMaxNodes,
            }, ct);

            var coupling = GraphMetricsComputer.ComputeProjectCoupling(graph);
            var dtos = projects.Select(p =>
            {
                var (afferent, efferent) = coupling.TryGetValue(p.ProjectId, out var c) ? (c.Afferent, c.Efferent) : (0, 0);
                var instability = afferent + efferent == 0 ? 0.0 : (double)efferent / (afferent + efferent);
                return new CouplingMetricDto(p.ProjectId, p.Name, afferent, efferent, Math.Round(instability, 2), GraphMetricsComputer.BandFor(instability, greenMax, yellowMax));
            }).ToList();

            return Results.Ok(new ApiEnvelope<IReadOnlyList<CouplingMetricDto>>(dtos));
        })
        .WithName("GetCouplingMetrics")
        .WithTags("Metrics")
        .Produces<ApiEnvelope<IReadOnlyList<CouplingMetricDto>>>();

        app.MapGet("/metrics/circular-dependencies", async (IGraphReader reader, CancellationToken ct) =>
        {
            var graph = await reader.GetSubgraphAsync(new GetSubgraphRequest
            {
                MaxNodes = GraphScopeResolver.UnscopedMaxNodes,
                PageSize = GraphScopeResolver.UnscopedMaxNodes,
            }, ct);

            // `cycle` repeats the closing project id (e.g. [a, b, a]); `length` is the distinct
            // project count (2), matching 05-rest-api.md Section 4.6's example.
            var cycles = GraphMetricsComputer.FindProjectCycles(graph);
            var dtos = cycles.Select(c => new CircularDependencyDto(c, c.Count - 1)).ToList();

            return Results.Ok(new ApiEnvelope<IReadOnlyList<CircularDependencyDto>>(dtos));
        })
        .WithName("GetCircularDependencies")
        .WithTags("Metrics")
        .Produces<ApiEnvelope<IReadOnlyList<CircularDependencyDto>>>();

        return app;
    }
}
