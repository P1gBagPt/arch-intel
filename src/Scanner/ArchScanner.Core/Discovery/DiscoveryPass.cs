using System.Collections.Concurrent;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;
using ArchScanner.Core.Heuristics;
using Microsoft.CodeAnalysis;

namespace ArchScanner.Core.Discovery;

public sealed record DiscoveryResult
{
    public required IReadOnlyList<ProjectDto> Projects { get; init; }
    public required IReadOnlyList<NodeDto> Nodes { get; init; }
    public required IReadOnlyList<EdgeDto> Edges { get; init; }
    public required SymbolRegistry Registry { get; init; }
    public required IReadOnlyDictionary<ProjectId, string> ArchProjectIdByRoslynId { get; init; }
    public required ProjectSignals Signals { get; init; }
}

/// <summary>
/// Pass 1 driver (Section 3.3). Two phases, both parallel across projects (Task.WhenAll —
/// scanOrder only affects output ordering, never compilation order):
///
///   Phase A collects each project's documents and its own local cross-type signals (DbSet&lt;T&gt;
///   membership, DI registrations, IOptions&lt;T&gt; unwrapping).
///   Phase B merges all of Phase A's signals solution-wide, THEN walks every project with
///   ArchDeclarationWalker using the merged signals.
///
/// The merge step exists because the class that makes a symbol interesting (the DbContext, the
/// composition root) commonly lives in a different project than the symbol itself (Infrastructure
/// vs. Domain) — classifying per-project alone would miss exactly that common layering.
/// </summary>
public sealed class DiscoveryPass
{
    private readonly NodeIdFactory _nodeIdFactory;
    private readonly IIdGenerator _idGenerator;

    public DiscoveryPass(IIdGenerator idGenerator)
    {
        _idGenerator = idGenerator;
        _nodeIdFactory = new NodeIdFactory(idGenerator);
    }

    public async Task<DiscoveryResult> RunAsync(
        string solutionRootDirectory,
        IReadOnlyList<Project> orderedProjects,
        IReadOnlyDictionary<ProjectId, string> projectLayers,
        CancellationToken ct = default)
    {
        var registry = new SymbolRegistry();
        var nodes = new ConcurrentBag<NodeDto>();
        var edges = new ConcurrentBag<EdgeDto>();
        var projectDtos = new ConcurrentBag<ProjectDto>();
        var archProjectIdByRoslynId = new ConcurrentDictionary<ProjectId, string>();

        foreach (var project in orderedProjects)
        {
            archProjectIdByRoslynId[project.Id] = _idGenerator.ProjectId(solutionRootDirectory, project.FilePath ?? project.Name);
        }

        var perProjectState = new ConcurrentDictionary<ProjectId, ProjectDiscoveryState>();

        await Task.WhenAll(orderedProjects.Select(project => Task.Run(async () =>
        {
            var archProjectId = archProjectIdByRoslynId[project.Id];
            var projectNodeId = _nodeIdFactory.ForProject(archProjectId);
            var isTestProject = IsTestProject(project);

            projectDtos.Add(new ProjectDto
            {
                ProjectId = archProjectId,
                Name = project.Name,
                Path = project.FilePath is null ? project.Name : NormalizeRelativePath(solutionRootDirectory, project.FilePath),
                ProjectType = isTestProject ? "Test" : null,
                Layer = projectLayers.TryGetValue(project.Id, out var layer) ? layer : null,
            });

            if (registry.TryRegister(GlobalSymbolKey.ForProject(archProjectId), projectNodeId))
            {
                nodes.Add(new NodeDto
                {
                    NodeId = projectNodeId,
                    ProjectId = archProjectId,
                    NodeType = NodeType.Project,
                    Name = project.Name,
                    FullName = project.Name,
                });
            }

            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is null)
            {
                perProjectState[project.Id] = new ProjectDiscoveryState(archProjectId, projectNodeId, [], new ProjectSignals());
                return;
            }

            var documents = new List<(SemanticModel SemanticModel, SyntaxNode Root, string RelativePath)>();
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
                documents.Add((semanticModel, root, NormalizeRelativePath(solutionRootDirectory, document.FilePath)));
            }

            var localSignals = ProjectSignalsScanner.Scan(documents.Select(d => (d.SemanticModel, d.Root)));
            perProjectState[project.Id] = new ProjectDiscoveryState(archProjectId, projectNodeId, documents, localSignals);
        }, ct)));

        var mergedSignals = ProjectSignals.Merge(perProjectState.Values.Select(s => s.LocalSignals));

        await Task.WhenAll(perProjectState.Values.Select(state => Task.Run(() =>
        {
            foreach (var (semanticModel, root, relativePath) in state.Documents)
            {
                var walker = new ArchDeclarationWalker(
                    semanticModel, state.ArchProjectId, relativePath, state.ProjectNodeId,
                    _nodeIdFactory, registry, _idGenerator, nodes, edges, mergedSignals);
                walker.Visit(root);
            }
        }, ct)));

        return new DiscoveryResult
        {
            Projects = projectDtos.ToList(),
            Nodes = nodes.ToList(),
            Edges = edges.ToList(),
            Registry = registry,
            ArchProjectIdByRoslynId = archProjectIdByRoslynId,
            Signals = mergedSignals,
        };
    }

    private sealed record ProjectDiscoveryState(
        string ArchProjectId,
        string ProjectNodeId,
        List<(SemanticModel SemanticModel, SyntaxNode Root, string RelativePath)> Documents,
        ProjectSignals LocalSignals);

    private static bool IsTestProject(Project project)
        => project.Name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
        || project.Name.EndsWith(".Test", StringComparison.OrdinalIgnoreCase)
        || project.Name.EndsWith(".UnitTests", StringComparison.OrdinalIgnoreCase)
        || project.Name.EndsWith(".IntegrationTests", StringComparison.OrdinalIgnoreCase)
        || project.MetadataReferences.Any(r => r.Display?.Contains("xunit", StringComparison.OrdinalIgnoreCase) == true)
        || project.MetadataReferences.Any(r => r.Display?.Contains("nunit", StringComparison.OrdinalIgnoreCase) == true)
        || project.MetadataReferences.Any(r => r.Display?.Contains("Microsoft.VisualStudio.TestPlatform", StringComparison.OrdinalIgnoreCase) == true);

    private static string NormalizeRelativePath(string root, string absolutePath)
        => Path.GetRelativePath(root, absolutePath).Replace('\\', '/');
}
