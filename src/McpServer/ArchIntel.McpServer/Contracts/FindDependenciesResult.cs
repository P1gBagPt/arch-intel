using System.Text.Json.Serialization;

namespace ArchIntel.McpServer.Contracts;

/// <summary>Output of `find_dependencies` (04-mcp-server.md Section 4). `RootNode` is null when
/// `symbolName` didn't resolve (not found, or ambiguous) — `Message` explains why, so the calling
/// agent gets a structured, recoverable result rather than an exception (Section 8.1).</summary>
public sealed record FindDependenciesResult
{
    [JsonPropertyName("rootNode")]
    public GraphNodeDto? RootNode { get; init; }

    [JsonPropertyName("dependencies")]
    public required IReadOnlyList<GraphEdgeResultDto> Dependencies { get; init; }

    [JsonPropertyName("truncated")]
    public required bool Truncated { get; init; }

    [JsonPropertyName("graphVersion")]
    public string? GraphVersion { get; init; }

    [JsonPropertyName("lastScannedAt")]
    public DateTimeOffset? LastScannedAt { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
