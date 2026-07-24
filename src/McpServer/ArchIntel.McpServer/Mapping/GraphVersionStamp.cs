using ArchIntel.GraphStore.Contracts;

namespace ArchIntel.McpServer.Mapping;

/// <summary>Formats scan metadata into the freshness stamp every tool response carries (04-mcp-server.md
/// Section 4's conventions), e.g. "2026-07-24T02:11:00Z#4821".</summary>
public static class GraphVersionStamp
{
    public static string? Format(ScanMetadataDto? metadata)
        => metadata is null ? null : $"{metadata.CompletedAt.UtcDateTime:yyyy-MM-ddTHH:mm:ss}Z#{metadata.ScanRunId}";
}
