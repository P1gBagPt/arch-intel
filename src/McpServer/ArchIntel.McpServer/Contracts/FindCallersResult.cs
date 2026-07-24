using System.Text.Json.Serialization;

namespace ArchIntel.McpServer.Contracts;

/// <summary>Output of `find_callers` — the reverse of `FindDependenciesResult` (Section 4).</summary>
public sealed record FindCallersResult
{
    [JsonPropertyName("rootNode")]
    public GraphNodeDto? RootNode { get; init; }

    [JsonPropertyName("callers")]
    public required IReadOnlyList<GraphEdgeResultDto> Callers { get; init; }

    [JsonPropertyName("truncated")]
    public required bool Truncated { get; init; }

    [JsonPropertyName("graphVersion")]
    public string? GraphVersion { get; init; }

    [JsonPropertyName("lastScannedAt")]
    public DateTimeOffset? LastScannedAt { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
