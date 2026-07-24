using System.ComponentModel;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchIntel.McpServer.Contracts;
using ArchIntel.McpServer.Mapping;
using ModelContextProtocol.Server;

namespace ArchIntel.McpServer.Tools;

/// <summary>
/// `find_dependencies` / `find_callers` (04-mcp-server.md Section 4, Phase 1). Thin wrappers over
/// IGraphReader.GetDependenciesAsync/GetCallersAsync — zero business logic of its own, per the
/// doc's Section 1 responsibility #3.
/// </summary>
[McpServerToolType]
public sealed class DependencyTools(IGraphReader graphReader)
{
    /// <summary>Safety cap on total nodes discovered across a multi-hop traversal — deliberately
    /// smaller than GetNeighborhoodAsync's default (500) since this feeds an LLM's context window.</summary>
    private const int MaxTraversalNodes = 200;

    [McpServerTool(Name = "find_dependencies", UseStructuredContent = true)]
    [Description("Returns the direct or transitive dependencies of a class, interface, service, or project node in the architecture graph.")]
    public async Task<FindDependenciesResult> FindDependencies(
        [Description("Simple or fully-qualified symbol name, e.g. 'OrderService' or 'PatternVision.Modules.Orders.OrderService'.")]
        string symbolName,
        [Description("How many relationship hops to traverse. 1 = direct dependencies only. Default 1, max 5.")]
        int depth = 1,
        [Description("Optional filter restricting which relationship kinds to follow, e.g. ['References','Injects']. Omit for all kinds.")]
        string[]? relationshipKinds = null,
        CancellationToken cancellationToken = default)
    {
        ValidateDepth(depth);
        var kinds = ParseRelationshipKinds(relationshipKinds);
        var resolver = new ProjectNameResolver(graphReader);

        var (rootNode, message) = await ResolveRootAsync(symbolName, cancellationToken);
        if (rootNode is null)
        {
            return new FindDependenciesResult { Dependencies = [], Truncated = false, Message = message };
        }

        var (dependencies, truncated) = await TraverseAsync(
            depth,
            (id, ct) => GetEdgesAsync(graphReader.GetDependenciesAsync, id, kinds, ct),
            rootNode.NodeId,
            resolver,
            cancellationToken);

        var metadata = await graphReader.GetLatestScanMetadataAsync(ct: cancellationToken);
        return new FindDependenciesResult
        {
            RootNode = await GraphNodeMapper.ToDtoAsync(rootNode, resolver, cancellationToken),
            Dependencies = dependencies,
            Truncated = truncated,
            GraphVersion = GraphVersionStamp.Format(metadata),
            LastScannedAt = metadata?.CompletedAt,
        };
    }

    [McpServerTool(Name = "find_callers", UseStructuredContent = true)]
    [Description("Returns who depends on / calls a class, interface, service, or project node — the reverse of find_dependencies.")]
    public async Task<FindCallersResult> FindCallers(
        [Description("Simple or fully-qualified symbol name, e.g. 'IOrderRepository'.")]
        string symbolName,
        [Description("How many relationship hops to traverse. 1 = direct callers only. Default 1, max 5.")]
        int depth = 1,
        CancellationToken cancellationToken = default)
    {
        ValidateDepth(depth);
        var resolver = new ProjectNameResolver(graphReader);

        var (rootNode, message) = await ResolveRootAsync(symbolName, cancellationToken);
        if (rootNode is null)
        {
            return new FindCallersResult { Callers = [], Truncated = false, Message = message };
        }

        var (callers, truncated) = await TraverseAsync(
            depth,
            (id, ct) => graphReader.GetCallersAsync(id, ct: ct),
            rootNode.NodeId,
            resolver,
            cancellationToken);

        var metadata = await graphReader.GetLatestScanMetadataAsync(ct: cancellationToken);
        return new FindCallersResult
        {
            RootNode = await GraphNodeMapper.ToDtoAsync(rootNode, resolver, cancellationToken),
            Callers = callers,
            Truncated = truncated,
            GraphVersion = GraphVersionStamp.Format(metadata),
            LastScannedAt = metadata?.CompletedAt,
        };
    }

    /// <summary>
    /// Level-by-level expansion reusing the existing 1-hop edge query, rather than the Graph Store's
    /// GetImpactAsync/GetNeighborhoodAsync — those only return a flat node list with no per-node
    /// relationship/depth, which would lose exactly the labeling depth=1 already provides. A visited
    /// set prevents revisiting a node (handles cycles); a node-count cap keeps a single tool response
    /// bounded for an LLM's context window.
    /// </summary>
    private static async Task<(List<GraphEdgeResultDto> Results, bool Truncated)> TraverseAsync(
        int maxDepth,
        Func<string, CancellationToken, Task<IReadOnlyList<EdgeWithNodeDto>>> getEdges,
        string rootNodeId,
        ProjectNameResolver resolver,
        CancellationToken ct)
    {
        var visited = new HashSet<string> { rootNodeId };
        var frontier = new List<string> { rootNodeId };
        var results = new List<GraphEdgeResultDto>();
        var truncated = false;

        for (var level = 1; level <= maxDepth && frontier.Count > 0 && !truncated; level++)
        {
            var nextFrontier = new List<string>();
            foreach (var nodeId in frontier)
            {
                if (truncated)
                {
                    break;
                }

                foreach (var edge in await getEdges(nodeId, ct))
                {
                    if (!visited.Add(edge.OtherNode.NodeId))
                    {
                        continue;
                    }

                    if (results.Count >= MaxTraversalNodes)
                    {
                        truncated = true;
                        break;
                    }

                    results.Add(new GraphEdgeResultDto
                    {
                        Relationship = edge.Edge.RelationshipType.ToString(),
                        Depth = level,
                        Node = await GraphNodeMapper.ToDtoAsync(edge.OtherNode, resolver, ct),
                    });
                    nextFrontier.Add(edge.OtherNode.NodeId);
                }
            }

            frontier = nextFrontier;
        }

        return (results, truncated);
    }

    /// <summary>Resolves symbolName to exactly one node, mirroring the CLI's `arch graph &lt;node&gt;`
    /// disambiguation rules (exact match preferred, ambiguous substring matches rejected with a
    /// listing) — kept consistent across every surface per the doc's "one brain, many callers" principle.</summary>
    private async Task<(NodeDto? Node, string? Message)> ResolveRootAsync(string symbolName, CancellationToken ct)
    {
        var exact = await graphReader.FindByNameAsync(symbolName, exactMatch: true, ct: ct);
        if (exact.Count == 1)
        {
            return (exact[0], null);
        }

        var matches = exact.Count > 1 ? exact : await graphReader.FindByNameAsync(symbolName, exactMatch: false, ct: ct);
        return matches.Count switch
        {
            0 => (null, $"No node found matching '{symbolName}'."),
            1 => (matches[0], null),
            _ => (null, $"Multiple nodes match '{symbolName}': {string.Join(", ", matches.Select(m => m.FullName))}. Be more specific."),
        };
    }

    private static async Task<IReadOnlyList<EdgeWithNodeDto>> GetEdgesAsync(
        Func<string, RelationshipType?, CancellationToken, Task<IReadOnlyList<EdgeWithNodeDto>>> query,
        string nodeId,
        IReadOnlyList<RelationshipType>? kinds,
        CancellationToken ct)
    {
        if (kinds is null || kinds.Count == 0)
        {
            return (await query(nodeId, null, ct)).ToList();
        }

        var seen = new HashSet<string>();
        var results = new List<EdgeWithNodeDto>();
        foreach (var kind in kinds)
        {
            foreach (var edge in await query(nodeId, kind, ct))
            {
                if (seen.Add(edge.Edge.EdgeId))
                {
                    results.Add(edge);
                }
            }
        }

        return results;
    }

    private static void ValidateDepth(int depth)
    {
        if (depth is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), depth, "depth must be between 1 and 5.");
        }
    }

    private static IReadOnlyList<RelationshipType>? ParseRelationshipKinds(string[]? relationshipKinds)
    {
        if (relationshipKinds is null || relationshipKinds.Length == 0)
        {
            return null;
        }

        var parsed = new List<RelationshipType>();
        foreach (var kind in relationshipKinds)
        {
            if (!Enum.TryParse<RelationshipType>(kind, ignoreCase: true, out var value))
            {
                throw new ArgumentException(
                    $"Unknown relationship kind '{kind}'. Valid values: {string.Join(", ", Enum.GetNames<RelationshipType>())}",
                    nameof(relationshipKinds));
            }

            parsed.Add(value);
        }

        return parsed;
    }
}
