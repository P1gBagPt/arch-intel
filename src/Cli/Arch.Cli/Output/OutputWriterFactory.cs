using System.CommandLine;

namespace Arch.Cli.Output;

public static class OutputWriterFactory
{
    public static IOutputWriter Create(ParseResult parseResult)
    {
        var format = GlobalOptions.ResolveFormat(parseResult);
        return format.Equals("json", StringComparison.OrdinalIgnoreCase)
            ? new JsonOutputWriter(indented: !Console.IsOutputRedirected)
            : new TableOutputWriter();
    }
}
