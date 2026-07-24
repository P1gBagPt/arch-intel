using System.Text.Json;

namespace Arch.Cli.Output;

/// <summary>Machine-facing renderer used when --format json (the default when stdout is redirected,
/// per Section 3.3's TTY-aware default, so `arch graph | jq .` works without an explicit flag).</summary>
public sealed class JsonOutputWriter : IOutputWriter
{
    private readonly JsonSerializerOptions _options;

    public JsonOutputWriter(bool indented)
    {
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = indented,
        };
    }

    public void WriteTable(TableData data)
    {
        var rows = data.Rows.Select(row => data.Headers
            .Zip(row, (header, cell) => (header, cell))
            .ToDictionary(pair => pair.header, pair => pair.cell));

        Console.WriteLine(JsonSerializer.Serialize(rows, _options));
    }

    public void WriteTree(TreeNodeData root) => Console.WriteLine(JsonSerializer.Serialize(root, _options));

    public void WriteObject<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, _options));

    public void WriteRaw(string text) => Console.WriteLine(text);

    public void WriteError(string message, Exception? ex = null)
        => Console.Error.WriteLine(JsonSerializer.Serialize(new { error = message, detail = ex?.Message }, _options));
}
