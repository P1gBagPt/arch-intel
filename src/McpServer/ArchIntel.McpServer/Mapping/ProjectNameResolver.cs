using ArchIntel.GraphStore.Contracts;

namespace ArchIntel.McpServer.Mapping;

/// <summary>Resolves a node's ProjectId to its display Name, caching ListProjectsAsync for the
/// lifetime of one tool call — Phase 1 graphs are small, so a per-call dictionary is plenty; no
/// need for anything fancier until Graph Store Phase 2's project metadata enrichment lands.</summary>
public sealed class ProjectNameResolver(IGraphReader reader)
{
    private IReadOnlyDictionary<string, string>? _cache;

    public async Task<string?> ResolveAsync(string projectId, CancellationToken ct)
    {
        _cache ??= (await reader.ListProjectsAsync(ct: ct)).ToDictionary(p => p.ProjectId, p => p.Name);
        return _cache.TryGetValue(projectId, out var name) ? name : null;
    }
}
