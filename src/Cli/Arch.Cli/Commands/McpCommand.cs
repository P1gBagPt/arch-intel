using System.CommandLine;
using Arch.Cli.Configuration;
using Arch.Cli.Output;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Sqlite;
using ArchIntel.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Arch.Cli.Commands;

/// <summary>
/// `arch mcp start`/`arch mcp status` (04-mcp-server.md Phase 1). `start` hosts the MCP server
/// in-process over stdio — one binary, matching this doc's own `.mcp.json` example
/// (`"command": "arch", "args": ["mcp", "start"]`) rather than a separate arch-mcp executable.
/// The tool classes themselves live in the standalone ArchIntel.McpServer library, so a dedicated
/// host could be split out later without touching them.
/// </summary>
public static class McpCommand
{
    private const string McpJsonSnippet = """
        {
          "mcpServers": {
            "arch": { "command": "arch", "args": ["mcp", "start"] }
          }
        }
        """;

    public static Command Build()
    {
        var mcp = new Command("mcp", "Bootstrap/inspect the local MCP server process.");

        var start = new Command("start", "Start the MCP server (stdio transport).");
        start.SetAction((parseResult, ct) => StartAsync(
            parseResult.GetValue(GlobalOptions.Config),
            parseResult.GetValue(GlobalOptions.Cwd)!,
            ct));

        var status = new Command("status", "Report whether the MCP server is configured and ready to start.");
        status.SetAction(parseResult => Status(
            parseResult.GetValue(GlobalOptions.Config),
            parseResult.GetValue(GlobalOptions.Cwd)!,
            OutputWriterFactory.Create(parseResult)));

        mcp.Subcommands.Add(start);
        mcp.Subcommands.Add(status);
        return mcp;
    }

    public static async Task<int> StartAsync(string? configPathOption, string cwd, CancellationToken ct)
    {
        // Stdout is about to become the MCP JSON-RPC transport, so any diagnostic output before
        // that point (and any error path below) must go to stderr — never Console.Out.
        var (dbPath, error) = TryResolveDbPath(configPathOption, cwd);
        if (error is not null)
        {
            Console.Error.WriteLine(error);
            return ExitCodes.ConfigurationError;
        }

        if (!File.Exists(dbPath))
        {
            Console.Error.WriteLine($"Graph database not found at {dbPath}. Run 'arch scan' first.");
            return ExitCodes.EnvironmentError;
        }

        var reader = new SqliteGraphReader(new SqliteConnectionFactory($"Data Source={dbPath}"));

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IGraphReader>(reader);
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<DependencyTools>()
            .WithTools<DiscoveryTools>();

        await builder.Build().RunAsync(ct);
        return ExitCodes.Success;
    }

    private static int Status(string? configPathOption, string cwd, IOutputWriter output)
    {
        var (dbPath, error) = TryResolveDbPath(configPathOption, cwd);
        if (error is not null)
        {
            output.WriteError(error);
            return ExitCodes.ConfigurationError;
        }

        var reachable = File.Exists(dbPath);
        output.WriteRaw(reachable
            ? $"✔ Graph database ready ({dbPath})"
            : $"✘ Graph database not found at {dbPath} — run 'arch scan' first");
        output.WriteRaw(string.Empty);
        output.WriteRaw("The MCP server is a per-invocation stdio child process (no background daemon to check) —");
        output.WriteRaw("add this to your MCP client config to launch it:");
        output.WriteRaw(McpJsonSnippet);

        return reachable ? ExitCodes.Success : ExitCodes.EnvironmentError;
    }

    private static (string? DbPath, string? Error) TryResolveDbPath(string? configPathOption, string cwd)
    {
        try
        {
            var resolved = ConfigDiscovery.Load(configPathOption, cwd);
            var configDir = Path.GetDirectoryName(resolved.Path)!;
            return (GraphStorePaths.ResolveDbPath(resolved.Config, configDir), null);
        }
        catch (Exception ex) when (ex is ConfigNotFoundException or FileNotFoundException)
        {
            return (null, ex.Message);
        }
    }
}
