using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace ArchScanner.Core.Workspace;

public sealed record WorkspaceDiagnostic(string Kind, string Message);

public sealed record WorkspaceLoadResult(
    MSBuildWorkspace Workspace,
    Solution Solution,
    IReadOnlyList<WorkspaceDiagnostic> Diagnostics);

public sealed class SolutionLoader
{
    /// <summary>
    /// Opens the solution. A single project failing to load (missing SDK, broken restore) degrades
    /// gracefully as a recorded diagnostic rather than throwing (Section 3.1) — the caller decides
    /// whether to abort or continue with the projects that did load.
    /// </summary>
    public async Task<WorkspaceLoadResult> LoadAsync(string solutionPath, CancellationToken ct = default)
    {
        MsBuildBootstrapper.EnsureRegistered();

        var diagnostics = new List<WorkspaceDiagnostic>();
        var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e =>
            diagnostics.Add(new WorkspaceDiagnostic(e.Diagnostic.Kind.ToString(), e.Diagnostic.Message)));

        var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct);

        return new WorkspaceLoadResult(workspace, solution, diagnostics);
    }
}
