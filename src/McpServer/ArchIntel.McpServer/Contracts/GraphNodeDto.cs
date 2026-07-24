using System.Text.Json.Serialization;

namespace ArchIntel.McpServer.Contracts;

/// <summary>The tool-facing node shape from 04-mcp-server.md Section 4 — deliberately smaller than
/// the Graph Store's own NodeDto (no metadata dictionary, project is a display name not an id) since
/// this is what an LLM-based agent actually needs to reason about a symbol.</summary>
public sealed record GraphNodeDto
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("project")]
    public string? Project { get; init; }
}
