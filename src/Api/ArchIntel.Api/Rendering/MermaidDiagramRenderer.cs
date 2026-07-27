using System.Text;
using ArchIntel.GraphStore.Contracts;

namespace ArchIntel.Api.Rendering;

/// <summary>Mermaid export for `POST /diagram` (05-rest-api.md Section 4.7) — API-owned, not a
/// Graph Store concern. Only `mermaid` is implemented; PlantUML/SVG are an open question.</summary>
public static class MermaidDiagramRenderer
{
    public static string Render(SubgraphDto subgraph)
    {
        var nodesById = subgraph.Nodes.ToDictionary(n => n.NodeId);
        var declared = new HashSet<string>();
        var touched = new HashSet<string>();
        var sb = new StringBuilder("graph TD");

        foreach (var edge in subgraph.Edges)
        {
            if (!nodesById.TryGetValue(edge.SourceId, out var source) || !nodesById.TryGetValue(edge.TargetId, out var target))
            {
                continue;
            }

            touched.Add(source.NodeId);
            touched.Add(target.NodeId);
            sb.Append('\n').Append("  ")
              .Append(RefOrLabel(source, declared)).Append(" -->|").Append(edge.RelationshipType).Append("| ")
              .Append(RefOrLabel(target, declared));
        }

        // Nodes untouched by any edge (isolated) still get a line so they appear in the diagram.
        foreach (var node in subgraph.Nodes.Where(n => !touched.Contains(n.NodeId)))
        {
            sb.Append('\n').Append("  ").Append(RefOrLabel(node, declared));
        }

        return sb.ToString();
    }

    private static string RefOrLabel(NodeDto node, HashSet<string> declared)
    {
        var id = MermaidId(node.NodeId);
        return declared.Add(node.NodeId) ? $"{id}[\"{Escape(node.Name)}\"]" : id;
    }

    // Node ids are content hashes and could start with a digit, which some Mermaid parsers reject
    // as a bare identifier — an "n" prefix keeps every id a safe alpha-leading token.
    private static string MermaidId(string nodeId) => $"n{nodeId}";

    private static string Escape(string text) => text.Replace("\"", "'");
}
