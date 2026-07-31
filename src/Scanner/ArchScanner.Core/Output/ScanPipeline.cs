using ArchIntel.GraphStore.Contracts;
using ArchScanner.Core.Configuration;
using ArchScanner.Core.Discovery;
using ArchScanner.Core.Resolution;
using ArchScanner.Core.Workspace;

namespace ArchScanner.Core.Output;

public sealed record ScanRunSummaryInfo
{
    public required long ScanRunId { get; init; }
    public required int ProjectCount { get; init; }
    public required int NodeCount { get; init; }
    public required int EdgeCount { get; init; }
}

public sealed record ScanResult
{
    public required ScanRunSummaryInfo Summary { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Orchestrates a full scan: bootstrap -> load solution -> scanOrder planning -> Pass 1
/// (parallel, barrier) -> Pass 2 -> write. Heuristic classification (Section 3.4) is folded into
/// Pass 1/2 rather than run as a separate stage — see ArchDeclarationWalker/RelationshipWalker.
/// </summary>
public sealed class ScanPipeline
{
    private readonly IGraphWriter _writer;
    private readonly IIdGenerator _idGenerator;

    public ScanPipeline(IGraphWriter writer, IIdGenerator idGenerator)
    {
        _writer = writer;
        _idGenerator = idGenerator;
    }

    public async Task<ScanResult> RunAsync(ScanConfig config, string repoId = "default", CancellationToken ct = default)
    {
        var loadResult = await new SolutionLoader().LoadAsync(config.Solution, ct);
        using var workspace = loadResult.Workspace;

        var solutionDirectory = Path.GetDirectoryName(Path.GetFullPath(config.Solution))
            ?? throw new InvalidOperationException($"Could not determine directory for solution '{config.Solution}'.");

        var warnings = loadResult.Diagnostics.Select(d => $"{d.Kind}: {d.Message}").ToList();

        var candidateProjects = loadResult.Solution.Projects
            .Where(p => !IsIgnored(p, solutionDirectory, config.Ignore))
            .ToList();

        var scanOrderResult = new ScanOrderPlanner().Plan(candidateProjects, config.ScanOrder);
        warnings.AddRange(scanOrderResult.Warnings);

        var discoveryResult = await new DiscoveryPass(_idGenerator)
            .RunAsync(solutionDirectory, scanOrderResult.OrderedProjects, scanOrderResult.ProjectLayers, ct);

        var resolutionResult = await new RelationshipResolver(discoveryResult.Registry, _idGenerator)
            .ResolveAsync(scanOrderResult.OrderedProjects, discoveryResult.ArchProjectIdByRoslynId, discoveryResult.Signals, ct);

        var allNodes = discoveryResult.Nodes.Concat(resolutionResult.Nodes).ToList();
        var allEdges = discoveryResult.Edges.Concat(resolutionResult.Edges).ToList();

        // Nodes/edges referencing a project or node id that was never actually written would hit
        // SQLite's FOREIGN KEY constraint (schema: nodes.project_id -> projects, edges.source_id/
        // target_id -> nodes) as an opaque "SQLite Error 19" with no indication of which row or
        // why. Validated here instead, against real solutions where discovery/resolution can
        // produce a dangling reference (e.g. a resolved relationship pointing at a symbol from an
        // ignored/excluded project) — the offending rows are dropped with a warning rather than
        // failing the whole scan.
        var projectIds = new HashSet<string>(discoveryResult.Projects.Select(p => p.ProjectId));
        var validNodes = new List<NodeDto>(allNodes.Count);
        foreach (var node in allNodes)
        {
            if (projectIds.Contains(node.ProjectId))
            {
                validNodes.Add(node);
            }
            else
            {
                warnings.Add($"Dropped node '{node.FullName}' ({node.NodeId}): references unknown project '{node.ProjectId}'.");
            }
        }

        var nodeIds = new HashSet<string>(validNodes.Select(n => n.NodeId));
        var validEdges = new List<EdgeDto>(allEdges.Count);
        foreach (var edge in allEdges)
        {
            if (nodeIds.Contains(edge.SourceId) && nodeIds.Contains(edge.TargetId))
            {
                validEdges.Add(edge);
            }
            else
            {
                var missing = !nodeIds.Contains(edge.SourceId) ? $"source '{edge.SourceId}'" : $"target '{edge.TargetId}'";
                warnings.Add($"Dropped {edge.RelationshipType} edge '{edge.EdgeId}': references unknown node {missing}.");
            }
        }

        allNodes = validNodes;
        allEdges = validEdges;

        var scanHandle = await _writer.BeginScanAsync(
            new BeginScanRequest { RepoId = repoId, ScanType = ScanType.Full, TriggeredBy = "cli" }, ct);

        try
        {
            foreach (var project in discoveryResult.Projects)
            {
                await _writer.UpsertProjectAsync(scanHandle, project, ct);
            }

            await _writer.UpsertNodesAsync(scanHandle, allNodes, ct);
            await _writer.UpsertEdgesAsync(scanHandle, allEdges, ct);
            await _writer.CompleteScanAsync(scanHandle, ct);
        }
        catch (Exception ex)
        {
            await _writer.FailScanAsync(scanHandle, ex.Message, ct);
            throw;
        }

        return new ScanResult
        {
            Summary = new ScanRunSummaryInfo
            {
                ScanRunId = scanHandle.ScanRunId,
                ProjectCount = discoveryResult.Projects.Count,
                NodeCount = allNodes.Count,
                EdgeCount = allEdges.Count,
            },
            Warnings = warnings,
        };
    }

    private static bool IsIgnored(Microsoft.CodeAnalysis.Project project, string solutionDirectory, IReadOnlyList<string> ignorePatterns)
    {
        if (project.FilePath is null || ignorePatterns.Count == 0)
        {
            return false;
        }

        var relativeSegments = Path.GetRelativePath(solutionDirectory, project.FilePath)
            .Replace('\\', '/')
            .Split('/');

        return ignorePatterns.Any(pattern => relativeSegments.Contains(pattern, StringComparer.OrdinalIgnoreCase));
    }
}
