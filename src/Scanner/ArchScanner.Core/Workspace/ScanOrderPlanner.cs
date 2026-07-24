using Microsoft.CodeAnalysis;

namespace ArchScanner.Core.Workspace;

public sealed record ScanOrderResult
{
    public required IReadOnlyList<Project> OrderedProjects { get; init; }
    public required IReadOnlyDictionary<ProjectId, string> ProjectLayers { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Buckets projects into scanOrder layers by substring match on project name (Section 3.2).
/// scanOrder only affects output ordering — never gates compilation (Pass 1 runs in parallel
/// regardless of this ordering).
/// </summary>
public sealed class ScanOrderPlanner
{
    public ScanOrderResult Plan(IEnumerable<Project> projects, IReadOnlyList<string> scanOrder)
    {
        var layers = new Dictionary<ProjectId, string>();
        var warnings = new List<string>();
        var buckets = scanOrder.ToDictionary(layer => layer, _ => new List<Project>(), StringComparer.OrdinalIgnoreCase);
        var unmatched = new List<Project>();

        foreach (var project in projects)
        {
            var matchedLayer = scanOrder.FirstOrDefault(layer =>
                project.Name.Contains(layer, StringComparison.OrdinalIgnoreCase));

            if (matchedLayer is not null)
            {
                buckets[matchedLayer].Add(project);
                layers[project.Id] = matchedLayer;
            }
            else
            {
                unmatched.Add(project);
                warnings.Add($"Project '{project.Name}' did not match any scanOrder layer; appended at the end.");
            }
        }

        var ordered = scanOrder.SelectMany(layer => buckets[layer]).Concat(unmatched).ToList();

        return new ScanOrderResult
        {
            OrderedProjects = ordered,
            ProjectLayers = layers,
            Warnings = warnings,
        };
    }
}
