using System.Text.Json.Serialization;

namespace ArchIntel.McpServer.Contracts;

/// <summary>One hop in a dependency/caller walk (Section 4's `GraphEdgeResult`).</summary>
public sealed record GraphEdgeResultDto
{
    [JsonPropertyName("relationship")]
    public required string Relationship { get; init; }

    [JsonPropertyName("depth")]
    public required int Depth { get; init; }

    [JsonPropertyName("node")]
    public required GraphNodeDto Node { get; init; }
}
