using ArchIntel.GraphStore.Contracts;

namespace ArchIntel.Api.Analysis;

/// <summary>Project-level coupling (afferent/efferent/instability) and circular-dependency
/// detection for `GET /metrics/coupling` and `GET /metrics/circular-dependencies`
/// (05-rest-api.md Section 4.6, Phase 3). IGraphReader has no built-in support for either —
/// `02-graph-store.md`'s own GetProjectMetricsAsync/GetCircularDependenciesAsync were never
/// actually added to the real IGraphReader contract — so both are computed here from one
/// whole-graph subgraph fetch rather than per-project fan-out queries.</summary>
public static class GraphMetricsComputer
{
    private const int MaxCycles = 20;
    private const int MaxCycleLength = 10;

    public sealed record ProjectCoupling(int Afferent, int Efferent);

    /// <summary>Afferent (Ca) = incoming cross-project edges; Efferent (Ce) = outgoing
    /// cross-project edges; edges within the same project don't count toward either.</summary>
    public static IReadOnlyDictionary<string, ProjectCoupling> ComputeProjectCoupling(SubgraphDto graph)
    {
        var projectOf = graph.Nodes.ToDictionary(n => n.NodeId, n => n.ProjectId);
        var afferent = new Dictionary<string, int>();
        var efferent = new Dictionary<string, int>();

        foreach (var edge in graph.Edges)
        {
            if (!projectOf.TryGetValue(edge.SourceId, out var sourceProject) || !projectOf.TryGetValue(edge.TargetId, out var targetProject))
            {
                continue;
            }

            if (sourceProject == targetProject)
            {
                continue;
            }

            efferent[sourceProject] = efferent.GetValueOrDefault(sourceProject) + 1;
            afferent[targetProject] = afferent.GetValueOrDefault(targetProject) + 1;
        }

        return projectOf.Values.Distinct().ToDictionary(p => p, p => new ProjectCoupling(afferent.GetValueOrDefault(p), efferent.GetValueOrDefault(p)));
    }

    public static string BandFor(double instability, double greenMax, double yellowMax) => instability switch
    {
        _ when instability <= greenMax => "Green",
        _ when instability <= yellowMax => "Yellow",
        _ => "Red",
    };

    /// <summary>Condenses the node graph to a project-level dependency graph (A -> B if any node in
    /// A depends on a node in B, A != B), then DFS's for simple cycles. Small project counts at
    /// local-dev scale make plain path-based DFS (same cycle-prevention idea as the Graph Store's
    /// own FindPathsAsync) fast enough without a real Tarjan's SCC implementation.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> FindProjectCycles(SubgraphDto graph)
    {
        var projectOf = graph.Nodes.ToDictionary(n => n.NodeId, n => n.ProjectId);
        var projectEdges = new Dictionary<string, HashSet<string>>();
        foreach (var edge in graph.Edges)
        {
            if (!projectOf.TryGetValue(edge.SourceId, out var source) || !projectOf.TryGetValue(edge.TargetId, out var target) || source == target)
            {
                continue;
            }

            if (!projectEdges.TryGetValue(source, out var targets))
            {
                targets = [];
                projectEdges[source] = targets;
            }

            targets.Add(target);
        }

        var cycles = new List<List<string>>();
        var seenCycleKeys = new HashSet<string>();

        void Dfs(string start, string current, List<string> path, HashSet<string> onPath)
        {
            if (cycles.Count >= MaxCycles || path.Count > MaxCycleLength)
            {
                return;
            }

            foreach (var next in projectEdges.GetValueOrDefault(current) ?? [])
            {
                if (cycles.Count >= MaxCycles)
                {
                    return;
                }

                if (next == start)
                {
                    var key = string.Join('|', path.Order(StringComparer.Ordinal));
                    if (seenCycleKeys.Add(key))
                    {
                        cycles.Add([.. path, start]);
                    }

                    continue;
                }

                if (onPath.Contains(next))
                {
                    continue;
                }

                path.Add(next);
                onPath.Add(next);
                Dfs(start, next, path, onPath);
                path.RemoveAt(path.Count - 1);
                onPath.Remove(next);
            }
        }

        foreach (var start in projectEdges.Keys)
        {
            if (cycles.Count >= MaxCycles)
            {
                break;
            }

            Dfs(start, start, [start], [start]);
        }

        return cycles;
    }
}
