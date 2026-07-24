using System.Text.Json.Serialization;

namespace ArchIntel.McpServer.Contracts;

/// <summary>Output of `find_service` (Section 4).</summary>
public sealed record FindServiceResult
{
    [JsonPropertyName("matches")]
    public required IReadOnlyList<GraphNodeDto> Matches { get; init; }

    [JsonPropertyName("truncated")]
    public required bool Truncated { get; init; }
}
