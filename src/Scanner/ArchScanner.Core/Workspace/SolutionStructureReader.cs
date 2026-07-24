using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.Build.Construction;

namespace ArchScanner.Core.Workspace;

public sealed record ProjectReferenceInfo(string Name, string AbsolutePath, IReadOnlyList<string> ReferencedProjectNames);

public sealed record SolutionStructure(IReadOnlyList<ProjectReferenceInfo> Projects);

/// <summary>
/// Lightweight solution/project-reference reader used by `arch init` (scanOrder inference) and
/// `arch doctor` (scanOrder validation) — parses via Microsoft.Build.Construction.SolutionFile and
/// raw &lt;ProjectReference&gt; XML rather than a full Roslyn/MSBuildWorkspace load (03-cli.md Section 5.3).
/// </summary>
public static class SolutionStructureReader
{
    /// <summary>
    /// Entry point deliberately contains no Microsoft.Build type references in its own signature/body —
    /// the JIT resolves a method's type references before running its first instruction, so if
    /// EnsureRegistered() lived in the same method as the SolutionFile usage below, the CLR would try
    /// (and fail) to load Microsoft.Build before MSBuildLocator ever got a chance to register.
    /// </summary>
    public static SolutionStructure Read(string solutionPath)
    {
        MsBuildBootstrapper.EnsureRegistered();
        return ReadCore(solutionPath);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SolutionStructure ReadCore(string solutionPath)
    {
        var solutionFile = SolutionFile.Parse(Path.GetFullPath(solutionPath));
        var msbuildProjects = solutionFile.ProjectsInOrder
            .Where(p => p.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat)
            .ToList();

        var projects = new List<ProjectReferenceInfo>();
        foreach (var project in msbuildProjects)
        {
            var referencedNames = ReadProjectReferences(project.AbsolutePath)
                .Select(refPath => msbuildProjects.FirstOrDefault(p => string.Equals(
                    Path.GetFullPath(p.AbsolutePath), refPath, StringComparison.OrdinalIgnoreCase))?.ProjectName)
                .Where(name => name is not null)
                .Select(name => name!)
                .ToList();

            projects.Add(new ProjectReferenceInfo(project.ProjectName, project.AbsolutePath, referencedNames));
        }

        return new SolutionStructure(projects);
    }

    private static IEnumerable<string> ReadProjectReferences(string csprojPath)
    {
        if (!File.Exists(csprojPath))
        {
            return [];
        }

        var projectDir = Path.GetDirectoryName(csprojPath)!;
        var doc = XDocument.Load(csprojPath);
        return doc.Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(Path.Combine(projectDir, include!.Replace('\\', Path.DirectorySeparatorChar))))
            .ToList();
    }

    /// <summary>
    /// Topologically sorts projects so dependencies come before dependents (Kahn's algorithm),
    /// matching the README's example ordering (Common, Domain, Application, Infrastructure, API, Tests).
    /// Falls back to appending remaining projects alphabetically if a cycle is detected, rather than
    /// throwing — real .csproj graphs are DAGs, but `arch init` shouldn't crash on a malformed one.
    /// </summary>
    public static IReadOnlyList<string> DeriveScanOrder(SolutionStructure structure)
    {
        var byName = structure.Projects.ToDictionary(p => p.Name);
        var remaining = new HashSet<string>(byName.Keys);
        var ordered = new List<string>();

        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(name => byName[name].ReferencedProjectNames.All(dep => !remaining.Contains(dep)))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ready.Count == 0)
            {
                ordered.AddRange(remaining.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
                break;
            }

            ordered.AddRange(ready);
            remaining.ExceptWith(ready);
        }

        return ordered;
    }

    /// <summary>
    /// Shortens exact project names down to the short layer labels ScanOrderPlanner actually matches
    /// on (substring match, Section 3.2) — e.g. "SampleErp.Common" → "Common" — by stripping the
    /// dot-separated prefix every project name shares. Falls back to the exact name for any project
    /// where stripping the shared prefix would leave nothing (e.g. all names identical).
    /// </summary>
    public static IReadOnlyList<string> ShortenToLayerLabels(IReadOnlyList<string> projectNames)
    {
        if (projectNames.Count == 0)
        {
            return projectNames;
        }

        var segmented = projectNames.Select(n => n.Split('.')).ToList();
        var minSegments = segmented.Min(s => s.Length);
        var sharedPrefixLength = 0;

        for (var i = 0; i < minSegments - 1; i++)
        {
            var segment = segmented[0][i];
            if (segmented.All(s => string.Equals(s[i], segment, StringComparison.OrdinalIgnoreCase)))
            {
                sharedPrefixLength++;
            }
            else
            {
                break;
            }
        }

        return projectNames
            .Select(n => string.Join('.', n.Split('.').Skip(sharedPrefixLength)))
            .ToList();
    }
}
