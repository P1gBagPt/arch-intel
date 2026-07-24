using Spectre.Console;

namespace Arch.Cli.Output;

/// <summary>Human-facing renderer used when --format table (the TTY default). Backed by Spectre.Console
/// for tables/trees/color; used purely as a rendering library, not as a competing app model.</summary>
public sealed class TableOutputWriter : IOutputWriter
{
    public void WriteTable(TableData data)
    {
        var table = new Table();
        foreach (var header in data.Headers)
        {
            table.AddColumn(Markup.Escape(header));
        }

        foreach (var row in data.Rows)
        {
            table.AddRow(row.Select(Markup.Escape).ToArray());
        }

        AnsiConsole.Write(table);
    }

    public void WriteTree(TreeNodeData root)
    {
        var tree = new Tree(Markup.Escape(root.Label));
        foreach (var child in root.Children)
        {
            AddNode(tree, child);
        }

        AnsiConsole.Write(tree);
    }

    public void WriteObject<T>(T value)
    {
        if (value is null)
        {
            AnsiConsole.MarkupLine("[grey](none)[/]");
            return;
        }

        var table = new Table().HideHeaders().AddColumn("Property").AddColumn("Value");
        foreach (var property in value.GetType().GetProperties())
        {
            var propertyValue = property.GetValue(value);
            table.AddRow(Markup.Escape(property.Name), Markup.Escape(Stringify(propertyValue)));
        }

        AnsiConsole.Write(table);
    }

    public void WriteRaw(string text) => Console.WriteLine(text);

    public void WriteError(string message, Exception? ex = null)
    {
        var detail = ex is not null ? $" ({ex.Message})" : string.Empty;
        Console.Error.WriteLine($"✘ {message}{detail}");
    }

    private static void AddNode(IHasTreeNodes parent, TreeNodeData node)
    {
        var child = parent.AddNode(Markup.Escape(node.Label));
        foreach (var grandchild in node.Children)
        {
            AddNode(child, grandchild);
        }
    }

    private static string Stringify(object? value) => value switch
    {
        null => string.Empty,
        IEnumerable<string> strings => string.Join(", ", strings),
        _ => value.ToString() ?? string.Empty,
    };
}
