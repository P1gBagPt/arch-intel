namespace ArchIntel.GraphStore.Contracts;

public sealed record ProjectDto
{
    public required string ProjectId { get; init; }
    public string RepoId { get; init; } = "default";
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string? TargetFramework { get; init; }
    public string? ProjectType { get; init; }
    public string? Layer { get; init; }
}
