using System.CommandLine;
using ArchScanner.Cli;

var configOption = new Option<string>("--config", "-c")
{
    Description = "Path to the arch.yaml scan config file.",
    Required = true,
};

var writerOption = new Option<string>("--writer", "-w")
{
    Description = "Graph writer to use: 'sqlite' or 'ndjson'.",
    DefaultValueFactory = _ => "sqlite",
};

var outputOption = new Option<string>("--output", "-o")
{
    Description = "SQLite db path (writer=sqlite) or output directory (writer=ndjson).",
    DefaultValueFactory = _ => "arch-graph.db",
};

var repoOption = new Option<string>("--repo-id")
{
    Description = "Repository id to scope this scan under.",
    DefaultValueFactory = _ => "default",
};

var scanCommand = new Command("scan", "Scans a .NET solution and writes the resulting architecture graph.")
{
    configOption,
    writerOption,
    outputOption,
    repoOption,
};

scanCommand.SetAction(async parseResult =>
{
    var configPath = parseResult.GetValue(configOption)!;
    var writerKind = parseResult.GetValue(writerOption)!;
    var output = parseResult.GetValue(outputOption)!;
    var repoId = parseResult.GetValue(repoOption)!;

    return await ScanCommand.RunAsync(configPath, writerKind, output, repoId, Console.Out);
});

var rootCommand = new RootCommand("arch — Architecture Intelligence Platform scanner CLI")
{
    scanCommand,
};

return await rootCommand.Parse(args).InvokeAsync();
