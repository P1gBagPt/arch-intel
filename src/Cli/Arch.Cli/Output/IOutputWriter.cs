namespace Arch.Cli.Output;

/// <summary>Tabular data: column headers plus rows of already-stringified cell values.</summary>
public sealed record TableData(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>A node in a rendered tree (e.g. `arch graph`'s project/dependency view).</summary>
public sealed record TreeNodeData(string Label, IReadOnlyList<TreeNodeData> Children)
{
    public TreeNodeData(string label) : this(label, []) { }
}

/// <summary>
/// The only surface command handlers render through (03-cli.md Section 3.4). A handler builds one
/// of these DTOs and calls one method — it never branches on the selected --format itself; each
/// implementation decides how its own format represents tables/trees/objects.
/// </summary>
public interface IOutputWriter
{
    void WriteTable(TableData data);
    void WriteTree(TreeNodeData root);
    void WriteObject<T>(T value);
    void WriteRaw(string text);
    void WriteError(string message, Exception? ex = null);
}
