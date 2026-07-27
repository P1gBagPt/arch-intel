namespace ArchIntel.Api.Contracts;

/// <summary>`POST /diagram` (05-rest-api.md Section 4.7). Only `format: "mermaid"` is implemented —
/// PlantUML/SVG are an open question (Section 10), left extensible but unimplemented.</summary>
public sealed record DiagramRequestDto(
    string? Scope,
    int Depth = 2,
    IReadOnlyList<string>? Kinds = null,
    string Format = "mermaid");

public sealed record DiagramResponseDto(string Format, string Content);
