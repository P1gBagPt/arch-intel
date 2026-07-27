using ArchIntel.Api.Problems;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;

namespace ArchIntel.Api.Resolution;

/// <summary>Shared `scope`/`depth`/`kinds` resolution behind `GET /graph` and `POST /diagram`
/// (05-rest-api.md Section 3.5 / 4.4 / 4.7) — both need the same "is scope a project id or a node
/// id" branching over IGraphReader, so it lives in one place rather than duplicated per endpoint.</summary>
public static class GraphScopeResolver
{
    /// <summary>Phase 1's "whole graph" cap, reused whenever no scope narrows the query.</summary>
    public const int UnscopedMaxNodes = 100_000;

    public static bool TryParseKinds(string? kindsCsv, out NodeType[]? kinds, out string? error)
    {
        kinds = null;
        error = null;
        if (string.IsNullOrWhiteSpace(kindsCsv))
        {
            return true;
        }

        var parts = kindsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsed = new List<NodeType>();
        foreach (var part in parts)
        {
            if (!Enum.TryParse<NodeType>(part, ignoreCase: true, out var value))
            {
                error = $"Unknown node kind '{part}'. Valid values: {string.Join(", ", Enum.GetNames<NodeType>())}";
                return false;
            }

            parsed.Add(value);
        }

        kinds = [.. parsed];
        return true;
    }

    /// <summary>Resolves `scope` to a bounded subgraph: no scope (or `full=true`) returns the whole
    /// graph; a known project id returns that project's subgraph; anything else is looked up as a
    /// node id and expanded via a `depth`-bounded neighborhood traversal.</summary>
    public static async Task<(SubgraphDto? Subgraph, IResult? Error)> ResolveAsync(
        IGraphReader reader, string? scope, int depth, NodeType[]? kinds, bool full, string instancePath, CancellationToken ct)
    {
        if (full || scope is null)
        {
            var subgraph = await reader.GetSubgraphAsync(new GetSubgraphRequest
            {
                NodeTypes = kinds,
                MaxNodes = UnscopedMaxNodes,
                PageSize = UnscopedMaxNodes,
            }, ct);
            return (subgraph, null);
        }

        var projects = await reader.ListProjectsAsync(ct: ct);
        if (projects.Any(p => p.ProjectId == scope))
        {
            var subgraph = await reader.GetSubgraphAsync(new GetSubgraphRequest
            {
                ProjectIds = [scope],
                NodeTypes = kinds,
                MaxNodes = UnscopedMaxNodes,
                PageSize = UnscopedMaxNodes,
            }, ct);
            return (subgraph, null);
        }

        var node = await reader.GetNodeAsync(scope, ct);
        if (node is null)
        {
            return (null, ProblemTypes.NodeNotFound(scope, instancePath));
        }

        var neighborhood = await reader.GetNeighborhoodAsync(new GetNeighborhoodRequest
        {
            SeedNodeId = scope,
            Depth = depth,
            NodeTypes = kinds,
        }, ct);
        return (neighborhood, null);
    }
}
