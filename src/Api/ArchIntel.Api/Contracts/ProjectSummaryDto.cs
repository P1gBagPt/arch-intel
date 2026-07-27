namespace ArchIntel.Api.Contracts;

public sealed record ProjectSummaryDto(
    string Id,
    string Name,
    string Path,
    string? ProjectType,
    string? Layer,
    string? TargetFramework);
