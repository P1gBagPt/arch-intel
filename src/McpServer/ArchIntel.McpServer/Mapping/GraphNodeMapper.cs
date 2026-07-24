using ArchIntel.GraphStore.Contracts;
using ArchIntel.McpServer.Contracts;

namespace ArchIntel.McpServer.Mapping;

public static class GraphNodeMapper
{
    public static async Task<GraphNodeDto> ToDtoAsync(NodeDto node, ProjectNameResolver projectNames, CancellationToken ct)
        => new()
        {
            Id = node.NodeId,
            Name = node.Name,
            Kind = node.NodeType.ToString(),
            Project = node.IsExternal ? null : await projectNames.ResolveAsync(node.ProjectId, ct),
        };
}
