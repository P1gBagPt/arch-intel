using ArchIntel.Api.Contracts;

namespace ArchIntel.Api.Analysis;

/// <summary>Pure scoring logic for `GET /quality-score`, kept separate from data-gathering
/// (QualityScoreEndpoints) so the heuristic itself — explicitly a documented guess per
/// 05-rest-api.md Section 10 — is easy to read and adjust in one place.</summary>
public static class QualityScoreComputer
{
    public static QualityScoreDto Compute(
        IReadOnlyDictionary<string, GraphMetricsComputer.ProjectCoupling> coupling,
        int cycleCount,
        int testClassCount,
        int serviceCount)
    {
        var couplingScore = CouplingScore(coupling);
        var circularScore = Math.Max(0, 100 - (20 * cycleCount));
        var testCoverageScore = serviceCount == 0 ? 100 : (int)Math.Min(100, Math.Round(100.0 * testClassCount / serviceCount));

        var factors = new List<QualityFactorDto>
        {
            new("Coupling", couplingScore, 0.4),
            new("CircularDependencies", circularScore, 0.3),
            new("TestCoverageProxy", testCoverageScore, 0.3),
        };

        var overall = (int)Math.Round(factors.Sum(f => f.Score * f.Weight));
        var band = overall switch
        {
            >= 80 => "Good",
            >= 60 => "Fair",
            _ => "Poor",
        };

        return new QualityScoreDto(overall, band, factors);
    }

    /// <summary>100 minus the average project instability (0-1) scaled to 0-100 — a highly
    /// unstable (high efferent-relative-to-afferent) codebase scores lower. Arbitrary but
    /// documented, same spirit as the coupling Green/Yellow/Red bands elsewhere in this API.</summary>
    private static int CouplingScore(IReadOnlyDictionary<string, GraphMetricsComputer.ProjectCoupling> coupling)
    {
        if (coupling.Count == 0)
        {
            return 100;
        }

        var averageInstability = coupling.Values.Average(c =>
        {
            var total = c.Afferent + c.Efferent;
            return total == 0 ? 0.0 : (double)c.Efferent / total;
        });

        return (int)Math.Round(100 * (1 - averageInstability));
    }
}
