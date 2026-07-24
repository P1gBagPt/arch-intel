using System.CommandLine;
using Arch.Cli;
using Arch.Cli.Commands;
using Spectre.Console;

if (args.Contains("--no-color") || Environment.GetEnvironmentVariable("NO_COLOR") is not null)
{
    AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings { ColorSystem = ColorSystemSupport.NoColors });
}

var rootCommand = new RootCommand("arch — Architecture Intelligence Platform CLI")
{
    InitCommand.Build(),
    ScanCommand.Build(),
    GraphCommand.Build(),
    DoctorCommand.Build(),
    McpCommand.Build(),
};

rootCommand.Options.Add(GlobalOptions.Config);
rootCommand.Options.Add(GlobalOptions.Format);
rootCommand.Options.Add(GlobalOptions.Verbosity);
rootCommand.Options.Add(GlobalOptions.NoColor);
rootCommand.Options.Add(GlobalOptions.Cwd);
rootCommand.Options.Add(GlobalOptions.Quiet);

return await rootCommand.Parse(args).InvokeAsync();
