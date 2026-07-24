using ArchIntel.GraphStore.Contracts.Enums;

namespace ArchIntel.GraphStore.Contracts;

/// <summary>Node DTO used for both write input and read output.</summary>
public sealed record NodeDto
{
    public required string NodeId { get; init; }
    public string RepoId { get; init; } = "default";
    public required string ProjectId { get; init; }
    public required NodeType NodeType { get; init; }
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public string? Namespace { get; init; }
    public string? FilePath { get; init; }
    public int? LineStart { get; init; }
    public int? LineEnd { get; init; }
    public bool IsExternal { get; init; } = false;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
