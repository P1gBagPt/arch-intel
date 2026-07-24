using System.Collections.Concurrent;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchScanner.Core.Discovery;
using ArchScanner.Core.Heuristics;
using ArchScanner.Core.Heuristics.WebApi;
using Microsoft.CodeAnalysis;

namespace ArchScanner.Core.Resolution;

public sealed record ResolutionResult
{
    public required IReadOnlyList<NodeDto> Nodes { get; init; }
    public required IReadOnlyList<EdgeDto> Edges { get; init; }
}

/// <summary>
/// Pass 2 driver: re-walks every project's syntax trees, resolving Implements/Inherits/Calls/
/// Injects/Consumes/Publishes edges, project-level References edges, DI-registration Owns edges,
/// and Minimal API endpoint nodes (the one Pass-2-stage heuristic that emits new nodes rather than
/// only edges, since a Map* call site has no corresponding Pass 1 declaration).
/// </summary>
public sealed class RelationshipResolver
{
    private readonly SymbolRegistry _registry;
    private readonly IIdGenerator _idGenerator;

    public RelationshipResolver(SymbolRegistry registry, IIdGenerator idGenerator)
    {
        _registry = registry;
        _idGenerator = idGenerator;
    }

    public async Task<ResolutionResult> ResolveAsync(
        IReadOnlyList<Project> orderedProjects,
        IReadOnlyDictionary<ProjectId, string> archProjectIdByRoslynId,
        ProjectSignals signals,
        CancellationToken ct = default)
    {
        var nodes = new ConcurrentBag<NodeDto>();
        var edges = new ConcurrentBag<EdgeDto>();

        await Task.WhenAll(orderedProjects.Select(project => Task.Run(async () =>
        {
            var compilation = await project.GetCompilationAsync(ct);
            var hasArchProjectId = archProjectIdByRoslynId.TryGetValue(project.Id, out var archProjectId);

            if (compilation is not null)
            {
                foreach (var document in project.Documents)
                {
                    if (document.FilePath is null || SourceFileFilter.IsGenerated(document.FilePath))
                    {
                        continue;
                    }

                    var tree = await document.GetSyntaxTreeAsync(ct);
                    if (tree is null)
                    {
                        continue;
                    }

                    var semanticModel = compilation.GetSemanticModel(tree);
                    var root = await tree.GetRootAsync(ct);

                    new RelationshipWalker(semanticModel, _registry, _idGenerator, edges).Visit(root);

                    if (hasArchProjectId)
                    {
                        MinimalApiDetector.Detect(semanticModel, root, archProjectId!, _idGenerator, _registry, nodes, edges);
                    }
                }
            }

            EmitProjectReferenceEdges(project, archProjectIdByRoslynId, edges);
        }, ct)));

        // Solution-wide, not per-project: a DI registration's interface/concrete pair may span
        // projects, and DiOwnsEdgeResolver's edge ids are deterministic, so resolving once here
        // (rather than once per project) is both correct and avoids redundant duplicate work.
        foreach (var edge in DiOwnsEdgeResolver.Resolve(signals, _registry, _idGenerator))
        {
            edges.Add(edge);
        }

        return new ResolutionResult { Nodes = nodes.ToList(), Edges = edges.ToList() };
    }

    private void EmitProjectReferenceEdges(
        Project project,
        IReadOnlyDictionary<ProjectId, string> archProjectIdByRoslynId,
        ConcurrentBag<EdgeDto> edges)
    {
        // References edges connect Project NODE ids, not the raw archProjectId strings — the
        // Project node's id is a further hash of archProjectId (NodeIdFactory.ForProject),
        // computed by DiscoveryPass and registered under this same key.
        if (!archProjectIdByRoslynId.TryGetValue(project.Id, out var sourceArchProjectId)
            || !_registry.TryGetNodeId(GlobalSymbolKey.ForProject(sourceArchProjectId), out var sourceProjectId))
        {
            return;
        }

        foreach (var projectRef in project.ProjectReferences)
        {
            if (!archProjectIdByRoslynId.TryGetValue(projectRef.ProjectId, out var targetArchProjectId)
                || !_registry.TryGetNodeId(GlobalSymbolKey.ForProject(targetArchProjectId), out var targetProjectId))
            {
                continue;
            }

            edges.Add(new EdgeDto
            {
                EdgeId = _idGenerator.EdgeId(sourceProjectId, targetProjectId, RelationshipType.References),
                SourceId = sourceProjectId,
                TargetId = targetProjectId,
                RelationshipType = RelationshipType.References,
            });
        }
    }
}
