using System.CommandLine;

namespace Arch.Cli;

/// <summary>
/// Options that apply to every command (03-cli.md Section 3.3), added once to the root command and
/// marked Recursive so any subcommand's action can read them via parseResult.GetValue(...).
/// </summary>
public static class GlobalOptions
{
    public static readonly Option<string?> Config = new("--config", "-c")
    {
        Description = "Path to arch.yml (default: discovered — see ARCH_CONFIG / walk-up)",
        Recursive = true,
    };

    public static readonly Option<string?> Format = new("--format", "-f")
    {
        Description = "table|json (default: table on a TTY, json when piped)",
        Recursive = true,
    };

    public static readonly Option<string> Verbosity = new("--verbosity", "-v")
    {
        Description = "quiet|minimal|normal|detailed|diagnostic",
        DefaultValueFactory = _ => "normal",
        Recursive = true,
    };

    public static readonly Option<bool> NoColor = new("--no-color")
    {
        Description = "Disable ANSI styling",
        Recursive = true,
    };

    public static readonly Option<string> Cwd = new("--cwd")
    {
        Description = "Run as if invoked from this directory",
        DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
        Recursive = true,
    };

    public static readonly Option<bool> Quiet = new("--quiet", "-q")
    {
        Description = "Suppress non-essential output",
        Recursive = true,
    };

    /// <summary>TTY-aware default: json when stdout is redirected, table otherwise (Section 3.3).</summary>
    public static string ResolveFormat(System.CommandLine.ParseResult parseResult)
        => parseResult.GetValue(Format) ?? (Console.IsOutputRedirected ? "json" : "table");
}
