using ArchIntel.GraphStore.Contracts;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ArchIntel.Api.HealthChecks;

/// <summary>`/health` (05-rest-api.md Section 8.3) — a lightweight "can we reach the graph store"
/// probe, not a full round-trip on every check. There's no Planning Service reachability probe
/// (the doc's other Phase 4 health check target) since PlaceholderPlanningService has no external
/// dependency to check yet — that becomes meaningful once a real LLM client exists.</summary>
public sealed class GraphStoreHealthCheck(IGraphReader reader) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await reader.ListProjectsAsync(ct: ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Graph Store is unreachable.", ex);
        }
    }
}
