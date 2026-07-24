using System.CommandLine;
using System.Diagnostics;
using Arch.Cli.Configuration;
using Arch.Cli.Output;
using ArchIntel.GraphStore.Sqlite;
using ArchScanner.Core.Workspace;
using Microsoft.Build.Locator;

namespace Arch.Cli.Commands;

/// <summary>`arch doctor` — diagnoses environment/config problems (03-cli.md Section 4, "arch doctor").
/// A flat list of checks rather than the doc's IDoctorCheck plugin hierarchy — six fixed checks
/// don't need an extensibility seam yet.</summary>
public static class DoctorCommand
{
    private sealed record CheckResult(string Name, bool Passed, string Message);

    public static Command Build()
    {
        var fixOption = new Option<bool>("--fix") { Description = "Attempt safe auto-fixes" };
        var command = new Command("doctor", "Diagnose environment/config problems before they surface confusingly elsewhere.")
        {
            fixOption,
        };

        command.SetAction((parseResult, ct) => RunAsync(
            parseResult.GetValue(GlobalOptions.Config),
            parseResult.GetValue(GlobalOptions.Cwd)!,
            OutputWriterFactory.Create(parseResult),
            ct));

        return command;
    }

    public static async Task<int> RunAsync(string? configPathOption, string cwd, IOutputWriter output, CancellationToken ct = default)
    {
        var results = new List<CheckResult>();

        ResolvedConfig? resolved = TryLoadConfig(configPathOption, cwd, results);
        var solutionPath = CheckSolutionExists(resolved, results);
        var msbuildOk = CheckMsBuildLocator(results);
        CheckScanOrderProjects(resolved, solutionPath, msbuildOk, results);
        await CheckGraphDatabaseReachable(resolved, results, ct);
        await CheckDotnetVersion(results, ct);

        foreach (var r in results)
        {
            output.WriteRaw($"{(r.Passed ? "✔" : "✘")} {r.Name} — {r.Message}");
        }

        var failed = results.Count(r => !r.Passed);
        output.WriteRaw(string.Empty);
        output.WriteRaw(failed == 0
            ? "All checks passed. Run 'arch scan' to build the architecture graph."
            : $"{failed} check(s) failed. See above for suggested fixes.");

        return failed == 0 ? ExitCodes.Success : ExitCodes.EnvironmentError;
    }

    private static ResolvedConfig? TryLoadConfig(string? configPathOption, string cwd, List<CheckResult> results)
    {
        try
        {
            var resolved = ConfigDiscovery.Load(configPathOption, cwd);
            results.Add(new CheckResult("arch.yml found and valid", true, resolved.Path));
            return resolved;
        }
        catch (Exception ex) when (ex is ConfigNotFoundException or FileNotFoundException)
        {
            results.Add(new CheckResult("arch.yml found and valid", false, $"{ex.Message} — run 'arch init' to fix"));
            return null;
        }
    }

    private static string? CheckSolutionExists(ResolvedConfig? resolved, List<CheckResult> results)
    {
        if (resolved is null)
        {
            results.Add(new CheckResult("Solution file exists", false, "skipped — no valid config"));
            return null;
        }

        var configDir = Path.GetDirectoryName(resolved.Path)!;
        var solutionPath = Path.GetFullPath(resolved.Config.Solution, configDir);
        var exists = File.Exists(solutionPath);
        results.Add(new CheckResult("Solution file exists", exists,
            exists ? solutionPath : $"expected at {solutionPath} — run 'arch init --solution <path>' to fix, or edit 'solution:' in arch.yml"));
        return exists ? solutionPath : null;
    }

    private static bool CheckMsBuildLocator(List<CheckResult> results)
    {
        try
        {
            var instances = MSBuildLocator.QueryVisualStudioInstances().ToList();
            if (instances.Count == 0)
            {
                results.Add(new CheckResult("MSBuild locator resolved", false, "no MSBuild/SDK instance found"));
                return false;
            }

            MsBuildBootstrapper.EnsureRegistered();
            results.Add(new CheckResult("MSBuild locator resolved", true, $"{instances[0].Name} {instances[0].Version}"));
            return true;
        }
        catch (Exception ex)
        {
            results.Add(new CheckResult("MSBuild locator resolved", false, ex.Message));
            return false;
        }
    }

    private static void CheckScanOrderProjects(ResolvedConfig? resolved, string? solutionPath, bool msbuildOk, List<CheckResult> results)
    {
        const string name = "All scanOrder projects found in solution";
        if (resolved is null || solutionPath is null || !msbuildOk)
        {
            results.Add(new CheckResult(name, false, "skipped — solution unavailable"));
            return;
        }

        try
        {
            var structure = SolutionStructureReader.Read(solutionPath);
            // Mirrors ScanOrderPlanner's own matching rule (substring, not exact equality) — scanOrder
            // entries are short layer labels like "Common", matched against full project names like
            // "SampleErp.Common", so an exact-equality check here would always report false positives.
            var missing = resolved.Config.ScanOrder
                .Where(layer => !structure.Projects.Any(p => p.Name.Contains(layer, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            results.Add(new CheckResult(name, missing.Count == 0,
                missing.Count == 0 ? $"{resolved.Config.ScanOrder.Count} projects" : $"no project matches: {string.Join(", ", missing)}"));
        }
        catch (Exception ex)
        {
            results.Add(new CheckResult(name, false, ex.Message));
        }
    }

    private static async Task CheckGraphDatabaseReachable(ResolvedConfig? resolved, List<CheckResult> results, CancellationToken ct)
    {
        const string name = "Graph database reachable";
        if (resolved is null)
        {
            results.Add(new CheckResult(name, false, "skipped — no valid config"));
            return;
        }

        var configDir = Path.GetDirectoryName(resolved.Path)!;
        var dbPath = GraphStorePaths.ResolveDbPath(resolved.Config, configDir);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            var factory = new SqliteConnectionFactory($"Data Source={dbPath}");
            using var connection = await factory.OpenConnectionAsync(ct);
            var sizeInfo = File.Exists(dbPath) ? $"{new FileInfo(dbPath).Length / 1024.0 / 1024.0:F1} MB" : "will be created on first scan";
            results.Add(new CheckResult(name, true, $"{dbPath} ({sizeInfo})"));
        }
        catch (Exception ex)
        {
            results.Add(new CheckResult(name, false, ex.Message));
        }
    }

    private static async Task CheckDotnetVersion(List<CheckResult> results, CancellationToken ct)
    {
        const string name = ".NET SDK version OK";
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet", "--version")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
            };
            process.Start();
            var versionOutput = (await process.StandardOutput.ReadToEndAsync(ct)).Trim();
            await process.WaitForExitAsync(ct);

            var ok = Version.TryParse(versionOutput.Split('-')[0], out var parsed) && parsed.Major >= 8;
            results.Add(new CheckResult(name, ok, $"{versionOutput} (>= 8.0.0 required)"));
        }
        catch (Exception ex)
        {
            results.Add(new CheckResult(name, false, ex.Message));
        }
    }
}
