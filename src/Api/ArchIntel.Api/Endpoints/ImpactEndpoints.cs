using ArchIntel.Api.Contracts;
using ArchIntel.Api.Problems;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;

namespace ArchIntel.Api.Endpoints;

/// <summary>`GET /impact` (05-rest-api.md Section 4.5). Phase 3 upgrades from direct dependents
/// only to a transitive, depth-annotated traversal. IGraphReader.GetTransitiveDependentsAsync
/// exists and would be the natural fit, but its ImpactResultDto returns a flat node list with no
/// per-node depth — exactly the gap 04-mcp-server.md's DependencyTools.TraverseAsync already hit
/// and solved by walking GetCallersAsync level-by-level instead. Reusing that pattern here so both
/// transports compute "depth" the same way.</summary>
public static class ImpactEndpoints
{
    private const int DefaultMaxDepth = 5;
    private const int MaxDepthCap = 10;
    private const int MaxAffectedNodes = 500;

    public static IEndpointRouteBuilder MapImpactEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/impact", async (string nodeId, int? maxDepth, IGraphReader reader, CancellationToken ct) =>
        {
            var node = await reader.GetNodeAsync(nodeId, ct);
            if (node is null)
            {
                return ProblemTypes.NodeNotFound(nodeId, $"/impact?nodeId={nodeId}");
            }

            var depth = Math.Clamp(maxDepth ?? DefaultMaxDepth, 1, MaxDepthCap);
            var affected = await TraverseAsync(reader, nodeId, depth, ct);

            var byKind = affected.GroupBy(a => a.Kind).ToDictionary(g => g.Key, g => g.Count());
            var dto = new ImpactResponseDto(node.NodeId, node.Name, affected, new ImpactSummaryDto(affected.Count, byKind));

            return Results.Ok(new ApiEnvelope<ImpactResponseDto>(dto));
        })
        .WithName("GetImpact")
        .WithTags("Impact")
        .Produces<ApiEnvelope<ImpactResponseDto>>()
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>Level-by-level expansion over GetCallersAsync (who depends on this node), same
    /// shape as DependencyTools.TraverseAsync. Contains edges are excluded — structural
    /// "namespace/class declares me" noise, not a real dependent (same fix as ServicesEndpoints'
    /// Callers field and the original Phase 2 /impact).</summary>
    private static async Task<List<AffectedNodeDto>> TraverseAsync(IGraphReader reader, string rootNodeId, int maxDepth, CancellationToken ct)
    {
        var visited = new HashSet<string> { rootNodeId };
        var frontier = new List<string> { rootNodeId };
        var results = new List<AffectedNodeDto>();

        for (var level = 1; level <= maxDepth && frontier.Count > 0 && results.Count < MaxAffectedNodes; level++)
        {
            var nextFrontier = new List<string>();
            foreach (var currentId in frontier)
            {
                if (results.Count >= MaxAffectedNodes)
                {
                    break;
                }

                foreach (var edge in await reader.GetCallersAsync(currentId, ct: ct))
                {
                    if (edge.Edge.RelationshipType == RelationshipType.Contains || !visited.Add(edge.OtherNode.NodeId))
                    {
                        continue;
                    }

                    if (results.Count >= MaxAffectedNodes)
                    {
                        break;
                    }

                    results.Add(new AffectedNodeDto(
                        edge.OtherNode.NodeId,
                        edge.OtherNode.NodeType.ToString(),
                        edge.OtherNode.Name,
                        edge.Edge.RelationshipType.ToString(),
                        level,
                        RiskLevelForDepth(level)));
                    nextFrontier.Add(edge.OtherNode.NodeId);
                }
            }

            frontier = nextFrontier;
        }

        return results;
    }

    private static string RiskLevelForDepth(int depth) => depth switch
    {
        1 => "Low",
        2 => "Medium",
        _ => "High",
    };
}
