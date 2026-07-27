using ArchIntel.GraphStore.Contracts.Enums;

namespace ArchIntel.Api.Mapping;

/// <summary>The "service-ish" NodeTypes shared by `GET /services` and `GET /metrics`'s
/// totalServices count — same set the MCP server's find_service tool uses
/// (ArchIntel.McpServer.Tools.DiscoveryTools.DefaultServiceKinds), plus MediatRHandler, so every
/// transport agrees on what counts as a "service".</summary>
public static class ServiceNodeTypes
{
    public static readonly NodeType[] Kinds =
    [
        NodeType.Service, NodeType.Controller, NodeType.HostedService,
        NodeType.MinimalApiEndpoint, NodeType.BackgroundWorker, NodeType.MediatRHandler,
    ];
}
