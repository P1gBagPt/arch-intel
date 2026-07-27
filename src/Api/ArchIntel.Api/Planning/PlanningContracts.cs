namespace ArchIntel.Api.Planning;

/// <summary>Shared contract shape 05-rest-api.md Section 1 describes as living in a
/// `PatternVision.Modules.Architecture.Planning` library shared with the MCP Server. That shared
/// library doesn't exist yet (no LLM client exists anywhere in this repo), so these types live
/// here for now — moving them out from under ArchIntel.Api into a standalone project is the
/// natural next step once the MCP Server needs the same contract (04-mcp-server.md).</summary>
public sealed record ImplementationPlanRequest(string Prompt, IReadOnlyList<string>? ScopeProjectIds = null);

public sealed record ImplementationPlanResult(
    IReadOnlyList<string> AffectedProjects,
    IReadOnlyList<string> NewFiles,
    IReadOnlyList<string> ModifiedServices,
    IReadOnlyList<string> DatabaseChanges,
    IReadOnlyList<string> TestsRequired,
    string RiskLevel,
    string EstimatedEffort);

public sealed record ArchitectureAnalysisRequest(string Question, IReadOnlyList<string>? ScopeNodeIds = null);

public sealed record ArchitectureAnalysisResult(
    string Summary,
    IReadOnlyList<string> AffectedNodeIds,
    IReadOnlyList<string> Recommendations);

/// <summary>05-rest-api.md Section 1's shared planning core: `GeneratePlanAsync`/`AnalyzeAsync`,
/// called identically by this REST API and (eventually) the MCP Server.</summary>
public interface IPlanningService
{
    Task<ImplementationPlanResult> GeneratePlanAsync(ImplementationPlanRequest request, CancellationToken ct);

    Task<ArchitectureAnalysisResult> AnalyzeAsync(ArchitectureAnalysisRequest request, CancellationToken ct);
}
