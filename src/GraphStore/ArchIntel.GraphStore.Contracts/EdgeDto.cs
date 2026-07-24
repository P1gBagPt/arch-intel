using ArchIntel.GraphStore.Contracts.Enums;

namespace ArchIntel.GraphStore.Contracts;

/// <summary>Edge DTO used for both write input and read output.</summary>
public sealed record EdgeDto
{
    public required string EdgeId { get; init; }
    public string RepoId { get; init; } = "default";
    public required string SourceId { get; init; }
    public required string TargetId { get; init; }
    public required RelationshipType RelationshipType { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
