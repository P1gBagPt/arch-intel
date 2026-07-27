namespace ArchIntel.Api.Contracts;

public sealed record ServiceSummaryDto(
    string Id,
    string Name,
    string Kind,
    string ProjectId,
    bool IsHostedService);
