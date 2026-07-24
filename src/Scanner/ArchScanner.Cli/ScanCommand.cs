using ArchIntel.GraphStore.Core;
using ArchIntel.GraphStore.Sqlite;
using ArchScanner.Core.Configuration;
using ArchScanner.Core.Output;

namespace ArchScanner.Cli;

/// <summary>
/// `arch scan` command handler (Section 10, Phase 1 task list). Not wired to a CLI-argument
/// library here — see the `README` note in Program.cs about the System.CommandLine preview
/// dependency; this class is the plain, directly-testable handler the CLI host wires up.
/// </summary>
public static class ScanCommand
{
    public static async Task<int> RunAsync(string configPath, string writerKind, string output, string repoId, TextWriter console)
    {
        if (!File.Exists(configPath))
        {
            console.WriteLine($"Config file not found: {configPath}");
            return 1;
        }

        var config = ScanConfigLoader.LoadFromFile(configPath);
        var idGenerator = new IdGenerator();

        await using var writer = await CreateWriterAsync(writerKind, output);

        var pipeline = new ScanPipeline(writer.Writer, idGenerator);

        console.WriteLine($"Scanning solution: {config.Solution}");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var result = await pipeline.RunAsync(config, repoId);

        stopwatch.Stop();

        console.WriteLine();
        console.WriteLine("Scan complete.");
        console.WriteLine($"  Scan run id:  {result.Summary.ScanRunId}");
        console.WriteLine($"  Projects:     {result.Summary.ProjectCount}");
        console.WriteLine($"  Nodes:        {result.Summary.NodeCount}");
        console.WriteLine($"  Edges:        {result.Summary.EdgeCount}");
        console.WriteLine($"  Elapsed:      {stopwatch.Elapsed.TotalSeconds:F2}s");

        if (result.Warnings.Count > 0)
        {
            console.WriteLine();
            console.WriteLine($"  Warnings ({result.Warnings.Count}):");
            foreach (var warning in result.Warnings)
            {
                console.WriteLine($"    - {warning}");
            }
        }

        return 0;
    }

    private static async Task<WriterHandle> CreateWriterAsync(string writerKind, string output)
    {
        switch (writerKind.ToLowerInvariant())
        {
            case "sqlite":
                var connectionFactory = new SqliteConnectionFactory($"Data Source={output}");
                await new MigrationRunner(connectionFactory).ApplyAsync();
                return new WriterHandle(new SqliteGraphWriter(connectionFactory), null);

            case "ndjson":
            default:
                return new WriterHandle(new NdjsonGraphWriter(output), null);
        }
    }

    private sealed record WriterHandle(ArchIntel.GraphStore.Contracts.IGraphWriter Writer, IDisposable? Disposable) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Disposable?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
