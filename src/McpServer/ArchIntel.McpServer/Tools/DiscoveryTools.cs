using System.ComponentModel;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchIntel.McpServer.Contracts;
using ArchIntel.McpServer.Mapping;
using ModelContextProtocol.Server;

namespace ArchIntel.McpServer.Tools;

/// <summary>`find_service` (04-mcp-server.md Section 4, Phase 1) — resolves a fuzzy/partial name to
/// service-ish graph nodes, the entry point before calling find_dependencies/find_callers.</summary>
[McpServerToolType]
public sealed class DiscoveryTools(IGraphReader graphReader)
{
    private static readonly NodeType[] DefaultServiceKinds =
    [
        NodeType.Service, NodeType.Controller, NodeType.HostedService,
        NodeType.MinimalApiEndpoint, NodeType.BackgroundWorker,
    ];

    [McpServerTool(Name = "find_service", UseStructuredContent = true)]
    [Description("Resolves a fuzzy/partial name to graph nodes classified as services (or the closest matching kind), used as the entry point before calling more specific tools.")]
    public async Task<FindServiceResult> FindService(
        [Description("Partial or full name, case-insensitive.")]
        string query,
        [Description("Optional narrower kind filter, e.g. ['Service','Repository']. Defaults to service-ish kinds (Service, Controller, HostedService, MinimalApiEndpoint, BackgroundWorker).")]
        string[]? kinds = null,
        [Description("Maximum number of matches to return.")]
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        if (maxResults is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), maxResults, "maxResults must be between 1 and 50.");
        }

        var kindFilters = ParseKinds(kinds) ?? DefaultServiceKinds;
        var resolver = new ProjectNameResolver(graphReader);

        var seen = new HashSet<string>();
        var matches = new List<NodeDto>();
        foreach (var kind in kindFilters)
        {
            foreach (var node in await graphReader.FindByNameAsync(query, kind, exactMatch: false, ct: cancellationToken))
            {
                if (seen.Add(node.NodeId))
                {
                    matches.Add(node);
                }
            }
        }

        var truncated = matches.Count > maxResults;
        var dtos = new List<GraphNodeDto>();
        foreach (var node in matches.Take(maxResults))
        {
            dtos.Add(await GraphNodeMapper.ToDtoAsync(node, resolver, cancellationToken));
        }

        return new FindServiceResult { Matches = dtos, Truncated = truncated };
    }

    private static NodeType[]? ParseKinds(string[]? kinds)
    {
        if (kinds is null || kinds.Length == 0)
        {
            return null;
        }

        var parsed = new List<NodeType>();
        foreach (var kind in kinds)
        {
            if (!Enum.TryParse<NodeType>(kind, ignoreCase: true, out var value))
            {
                throw new ArgumentException(
                    $"Unknown node kind '{kind}'. Valid values: {string.Join(", ", Enum.GetNames<NodeType>())}",
                    nameof(kinds));
            }

            parsed.Add(value);
        }

        return parsed.ToArray();
    }
}
