using System.CommandLine;
using Arch.Cli.Output;

namespace Arch.Cli.Commands;

/// <summary>
/// `arch mcp start`/`arch mcp status` — honest stub. The MCP Server component (04-mcp-server.md)
/// hasn't been built yet, so rather than faking a working bootstrap, this reports that plainly
/// and exits with the "not yet implemented for this phase" code (Section 3.5).
/// </summary>
public static class McpCommand
{
    public static Command Build()
    {
        var mcp = new Command("mcp", "Bootstrap/inspect the local MCP server process.");

        var start = new Command("start", "Start the MCP server (stdio transport).");
        start.SetAction(parseResult => Run(OutputWriterFactory.Create(parseResult)));

        var status = new Command("status", "Report whether the MCP server is running.");
        status.SetAction(parseResult => Run(OutputWriterFactory.Create(parseResult)));

        mcp.Subcommands.Add(start);
        mcp.Subcommands.Add(status);
        return mcp;
    }

    private static int Run(IOutputWriter output)
    {
        output.WriteError("MCP server not yet implemented (see implementation-plans/04-mcp-server.md).");
        return ExitCodes.UserError;
    }
}
