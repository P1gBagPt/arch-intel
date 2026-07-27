using ArchIntel.Api.Contracts;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;

namespace ArchIntel.Api.Mapping;

public static class DtoMappers
{
    public static ProjectSummaryDto ToDto(this ProjectDto project) => new(
        project.ProjectId,
        project.Name,
        project.Path,
        project.ProjectType,
        project.Layer,
        project.TargetFramework);

    public static ServiceSummaryDto ToServiceDto(this NodeDto node) => new(
        node.NodeId,
        node.Name,
        node.NodeType.ToString(),
        node.ProjectId,
        node.NodeType == NodeType.HostedService);

    public static GraphNodeDto ToGraphNodeDto(this NodeDto node) => new(node.NodeId, node.NodeType.ToString(), node.Name);

    public static GraphEdgeDto ToGraphEdgeDto(this EdgeDto edge) => new(edge.SourceId, edge.TargetId, edge.RelationshipType.ToString());

    public static NodeRefDto ToNodeRefDto(this NodeDto node, string? relation = null) => new(node.NodeId, node.NodeType.ToString(), node.Name, relation);

    /// <summary>`OtherNode` is the dependency (for GetDependenciesAsync results) or the caller (for
    /// GetCallersAsync results) — either way, the node on the *other* end of the edge from whichever
    /// node the caller queried.</summary>
    public static NodeRefDto ToNodeRefDto(this EdgeWithNodeDto edge) => edge.OtherNode.ToNodeRefDto(edge.Edge.RelationshipType.ToString());
}
