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
    [McpServerTool(Name = "find_dependencies", UseStructuredContent = true)]
    [Description("Returns the direct dependencies of a class, interface, service, or project node in the architecture graph.")]
    public async Task<FindDependenciesResult> FindDependencies(
        [Description("Simple or fully-qualified symbol name, e.g. 'OrderService' or 'PatternVision.Modules.Orders.OrderService'.")]
        string symbolName,
        [Description("How many relationship hops to traverse. Currently limited to 1 (direct dependencies only) until Graph Store Phase 2 traversal ships; accepted range 1-5 for forward compatibility.")]
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

        var edges = await GetEdgesAsync(graphReader.GetDependenciesAsync, rootNode.NodeId, kinds, cancellationToken);
        var dependencies = new List<GraphEdgeResultDto>();
        foreach (var edge in edges)
        {
            dependencies.Add(new GraphEdgeResultDto
            {
                Relationship = edge.Edge.RelationshipType.ToString(),
                Depth = 1,
                Node = await GraphNodeMapper.ToDtoAsync(edge.OtherNode, resolver, cancellationToken),
            });
        }

        var metadata = await graphReader.GetLatestScanMetadataAsync(ct: cancellationToken);
        return new FindDependenciesResult
        {
            RootNode = await GraphNodeMapper.ToDtoAsync(rootNode, resolver, cancellationToken),
            Dependencies = dependencies,
            Truncated = false,
            GraphVersion = GraphVersionStamp.Format(metadata),
            LastScannedAt = metadata?.CompletedAt,
            Message = depth > 1 ? "depth beyond 1 requires Graph Store Phase 2 traversal support; returning 1-hop results" : null,
        };
    }

    [McpServerTool(Name = "find_callers", UseStructuredContent = true)]
    [Description("Returns who depends on / calls a class, interface, service, or project node — the reverse of find_dependencies.")]
    public async Task<FindCallersResult> FindCallers(
        [Description("Simple or fully-qualified symbol name, e.g. 'IOrderRepository'.")]
        string symbolName,
        [Description("How many relationship hops to traverse. Currently limited to 1 until Graph Store Phase 2 traversal ships; accepted range 1-5 for forward compatibility.")]
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

        var edges = await graphReader.GetCallersAsync(rootNode.NodeId, ct: cancellationToken);
        var callers = new List<GraphEdgeResultDto>();
        foreach (var edge in edges)
        {
            callers.Add(new GraphEdgeResultDto
            {
                Relationship = edge.Edge.RelationshipType.ToString(),
                Depth = 1,
                Node = await GraphNodeMapper.ToDtoAsync(edge.OtherNode, resolver, cancellationToken),
            });
        }

        var metadata = await graphReader.GetLatestScanMetadataAsync(ct: cancellationToken);
        return new FindCallersResult
        {
            RootNode = await GraphNodeMapper.ToDtoAsync(rootNode, resolver, cancellationToken),
            Callers = callers,
            Truncated = false,
            GraphVersion = GraphVersionStamp.Format(metadata),
            LastScannedAt = metadata?.CompletedAt,
            Message = depth > 1 ? "depth beyond 1 requires Graph Store Phase 2 traversal support; returning 1-hop results" : null,
        };
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

    private static async Task<List<EdgeWithNodeDto>> GetEdgesAsync(
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
