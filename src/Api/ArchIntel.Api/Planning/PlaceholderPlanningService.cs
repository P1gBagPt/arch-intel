using ArchIntel.Api.Mapping;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Contracts.Enums;

namespace ArchIntel.Api.Planning;

/// <summary>No LLM integration exists yet anywhere in this repo (no API key, no client) — per
/// 05-rest-api.md Section 1, the REST API only shapes the HTTP contract around the Planning
/// Service's result and explicitly treats plan-synthesis logic as out of scope for this document.
///
/// GeneratePlanAsync can't respond to a free-text prompt without an LLM, so it returns a
/// structural placeholder derived purely from the requested scope (or the whole graph if
/// unscoped) — clearly labeled via RiskLevel/EstimatedEffort so no caller mistakes it for a real
/// plan. AnalyzeAsync is different: "what would this affect" doesn't actually require an LLM, only
/// natural-language summarization does — so it runs a real IGraphReader.GetTransitiveDependentsAsync
/// traversal and only templates the prose (Summary/Recommendations), which are the parts that
/// really would benefit from real synthesis later.</summary>
public sealed class PlaceholderPlanningService(IGraphReader reader) : IPlanningService
{
    private const int AnalysisMaxDepth = 5;
    private const string PlaceholderRiskLevel = "Unknown";
    private const string PlaceholderEffort = "Unknown — placeholder Planning Service, no LLM wired yet";

    public async Task<ImplementationPlanResult> GeneratePlanAsync(ImplementationPlanRequest request, CancellationToken ct)
    {
        var projects = await reader.ListProjectsAsync(ct: ct);
        var scopedProjects = request.ScopeProjectIds is { Count: > 0 }
            ? projects.Where(p => request.ScopeProjectIds.Contains(p.ProjectId)).ToList()
            : projects;

        var modifiedServices = new List<string>();
        var testsRequired = new List<string>();
        foreach (var project in scopedProjects)
        {
            var nodes = await reader.GetNodesByProjectAsync(project.ProjectId, nodeType: null, ct: ct);
            modifiedServices.AddRange(nodes.Where(n => ServiceNodeTypes.Kinds.Contains(n.NodeType)).Select(n => n.Name));
            testsRequired.AddRange(nodes.Where(n => n.NodeType == NodeType.TestClass).Select(n => n.Name));
        }

        return new ImplementationPlanResult(
            AffectedProjects: scopedProjects.Select(p => p.ProjectId).ToList(),
            NewFiles: [], // can't infer what new files a prompt implies without an LLM
            ModifiedServices: modifiedServices,
            DatabaseChanges: [], // ditto
            TestsRequired: testsRequired,
            RiskLevel: PlaceholderRiskLevel,
            EstimatedEffort: PlaceholderEffort);
    }

    public async Task<ArchitectureAnalysisResult> AnalyzeAsync(ArchitectureAnalysisRequest request, CancellationToken ct)
    {
        var scopeNodeId = request.ScopeNodeIds?.FirstOrDefault();
        if (scopeNodeId is null)
        {
            return new ArchitectureAnalysisResult(
                Summary: "No scopeNodeIds provided — nothing to analyze. Provide at least one node id to compute its downstream impact.",
                AffectedNodeIds: [],
                Recommendations: []);
        }

        var node = await reader.GetNodeAsync(scopeNodeId, ct);
        if (node is null)
        {
            return new ArchitectureAnalysisResult(
                Summary: $"No node with id '{scopeNodeId}' exists in the current graph snapshot.",
                AffectedNodeIds: [],
                Recommendations: []);
        }

        var impact = await reader.GetTransitiveDependentsAsync(scopeNodeId, AnalysisMaxDepth, ct: ct);
        var affected = impact.AffectedNodes.Where(n => n.NodeType != NodeType.Namespace).ToList();

        var summary = affected.Count == 0
            ? $"Nothing in the current graph transitively depends on '{node.Name}' (within {AnalysisMaxDepth} hops)."
            : $"Removing or changing '{node.Name}' would affect {affected.Count} downstream node(s), including: "
              + string.Join(", ", affected.Take(5).Select(n => n.Name)) + (affected.Count > 5 ? ", ..." : ".");

        var recommendations = affected.Count == 0
            ? []
            : new List<string>
            {
                "Review each affected node listed above before proceeding.",
                "Introduce a facade/adapter if consumers can't migrate atomically.",
                "Add characterization tests for affected nodes before refactoring.",
            };

        return new ArchitectureAnalysisResult(summary, affected.Select(n => n.NodeId).ToList(), recommendations);
    }
}
