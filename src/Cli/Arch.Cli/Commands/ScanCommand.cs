using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using Arch.Cli.Configuration;
using Arch.Cli.Output;
using ArchScanner.Core.Configuration;
using ArchIntel.GraphStore.Core;
using ArchIntel.GraphStore.Sqlite;
using ArchScanner.Core.Output;
using Spectre.Console;

namespace Arch.Cli.Commands;

public sealed record ScanSummaryDto
{
    public required string Status { get; init; }
    public required long ScanRunId { get; init; }
    public required int Projects { get; init; }
    public required int Nodes { get; init; }
    public required int Edges { get; init; }
    public required double DurationMs { get; init; }
    public required int Warnings { get; init; }
}

/// <summary>`arch scan` — full solution scan, populating/replacing the Graph Store (03-cli.md Section 4, "arch scan").</summary>
public static class ScanCommand
{
    public static Command Build()
    {
        var failOnWarningOption = new Option<bool>("--fail-on-warning") { Description = "Exit non-zero if the scanner reports warnings" };
        var outputSummaryOption = new Option<string?>("--output-summary") { Description = "Write a JSON scan summary to a file in addition to stdout" };
        var repoOption = new Option<string>("--repo-id") { Description = "Repository id to scope this scan under", DefaultValueFactory = _ => "default" };

        var command = new Command("scan", "Perform a full solution scan, populating/replacing the Graph Store.")
        {
            failOnWarningOption, outputSummaryOption, repoOption,
        };

        command.SetAction((parseResult, ct) => RunAsync(
            parseResult.GetValue(GlobalOptions.Config),
            parseResult.GetValue(GlobalOptions.Cwd)!,
            parseResult.GetValue(failOnWarningOption),
            parseResult.GetValue(outputSummaryOption),
            parseResult.GetValue(repoOption)!,
            OutputWriterFactory.Create(parseResult),
            ct));

        return command;
    }

    public static async Task<int> RunAsync(
        string? configPathOption,
        string cwd,
        bool failOnWarning,
        string? outputSummaryPath,
        string repoId,
        IOutputWriter output,
        CancellationToken ct = default)
    {
        ResolvedConfig resolved;
        try
        {
            resolved = ConfigDiscovery.Load(configPathOption, cwd);
        }
        catch (Exception ex) when (ex is ConfigNotFoundException or FileNotFoundException)
        {
            output.WriteError(ex.Message);
            return ExitCodes.ConfigurationError;
        }

        var configDir = Path.GetDirectoryName(resolved.Path)!;
        var solutionPath = Path.GetFullPath(resolved.Config.Solution, configDir);
        if (!File.Exists(solutionPath))
        {
            output.WriteError($"Solution file not found: {solutionPath}");
            return ExitCodes.ConfigurationError;
        }

        var dbPath = GraphStorePaths.ResolveDbPath(resolved.Config, configDir);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var idGenerator = new IdGenerator();
        var connectionFactory = new SqliteConnectionFactory($"Data Source={dbPath}");
        await new MigrationRunner(connectionFactory).ApplyAsync(ct);
        var writer = new SqliteGraphWriter(connectionFactory);
        var pipeline = new ScanPipeline(writer, idGenerator);

        var effectiveConfig = new ScanConfig
        {
            Solution = solutionPath,
            ScanOrder = resolved.Config.ScanOrder,
            Ignore = resolved.Config.Ignore,
            Languages = resolved.Config.Languages,
            Rules = resolved.Config.Rules,
            Storage = resolved.Config.Storage,
        };

        var stopwatch = Stopwatch.StartNew();
        ScanResult scanResult = null!;
        try
        {
            await AnsiConsole.Status().StartAsync(
                $"Scanning {Path.GetFileName(solutionPath)}...",
                async _ => scanResult = await pipeline.RunAsync(effectiveConfig, repoId, ct));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            output.WriteError("Scan failed", ex);
            return ExitCodes.ScanFailed;
        }

        stopwatch.Stop();

        var summary = new ScanSummaryDto
        {
            Status = "success",
            ScanRunId = scanResult.Summary.ScanRunId,
            Projects = scanResult.Summary.ProjectCount,
            Nodes = scanResult.Summary.NodeCount,
            Edges = scanResult.Summary.EdgeCount,
            DurationMs = stopwatch.Elapsed.TotalMilliseconds,
            Warnings = scanResult.Warnings.Count,
        };

        output.WriteObject(summary);
        if (scanResult.Warnings.Count > 0)
        {
            foreach (var warning in scanResult.Warnings)
            {
                output.WriteRaw($"  - {warning}");
            }
        }

        output.WriteRaw($"Graph written to {dbPath}");

        if (outputSummaryPath is not null)
        {
            await File.WriteAllTextAsync(
                outputSummaryPath,
                JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }),
                ct);
        }

        return failOnWarning && scanResult.Warnings.Count > 0 ? ExitCodes.UserError : ExitCodes.Success;
    }
}
