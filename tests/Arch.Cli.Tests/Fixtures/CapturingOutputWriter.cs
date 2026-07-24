using Arch.Cli.Output;

namespace Arch.Cli.Tests.Fixtures;

/// <summary>Records what a command wrote instead of rendering it, so tests can assert on content
/// without depending on Spectre.Console's console-buffer capture machinery.</summary>
public sealed class CapturingOutputWriter : IOutputWriter
{
    public List<TableData> Tables { get; } = [];
    public List<TreeNodeData> Trees { get; } = [];
    public List<object?> Objects { get; } = [];
    public List<string> Lines { get; } = [];
    public List<(string Message, Exception? Ex)> Errors { get; } = [];

    public void WriteTable(TableData data) => Tables.Add(data);
    public void WriteTree(TreeNodeData root) => Trees.Add(root);
    public void WriteObject<T>(T value) => Objects.Add(value);
    public void WriteRaw(string text) => Lines.Add(text);
    public void WriteError(string message, Exception? ex = null) => Errors.Add((message, ex));
}
