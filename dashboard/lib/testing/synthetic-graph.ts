import type { GraphEdge, GraphNode } from "@/types/graph";

const KINDS = ["Project", "Class", "Interface", "Method", "TestClass", "TestMethod", "Service"];
const RELATIONS = [
  "References",
  "Calls",
  "Implements",
  "Inherits",
  "Injects",
  "Uses",
  "Publishes",
  "Consumes",
  "Owns",
  "Contains",
];

// Synthesizes a graph at real-world scale (06-dashboard.md §11's 1k/5k/10k/20k load-test
// checkpoints) since no scanned solution in this repo is anywhere near that size. Edges are
// mostly local (nearby node indices, approximating containment/call locality within a class or
// file) with a smaller random long-range share (approximating cross-project references) — a
// uniform-random edge set would be unrealistically well-mixed and easier for a force-directed
// layout to settle than a real dependency graph.
export function generateSyntheticGraph(
  nodeCount: number,
  avgEdgesPerNode = 1.4,
): { nodes: GraphNode[]; edges: GraphEdge[] } {
  const nodes: GraphNode[] = Array.from({ length: nodeCount }, (_, i) => {
    const kind = KINDS[i % KINDS.length];
    return { id: `n${i}`, kind, name: `${kind}${i}` };
  });

  const edgeCount = Math.round(nodeCount * avgEdgesPerNode);
  const edges: GraphEdge[] = [];
  const seen = new Set<string>();

  for (let i = 0; i < edgeCount; i++) {
    const fromIdx = Math.floor(Math.random() * nodeCount);
    const local = Math.random() < 0.8;
    const toIdx = local
      ? Math.max(0, Math.min(nodeCount - 1, fromIdx + Math.floor(Math.random() * 11) - 5))
      : Math.floor(Math.random() * nodeCount);

    if (toIdx === fromIdx) continue;
    const type = RELATIONS[Math.floor(Math.random() * RELATIONS.length)];
    const key = `${fromIdx}->${toIdx}:${type}`;
    if (seen.has(key)) continue;
    seen.add(key);

    edges.push({ fromId: `n${fromIdx}`, toId: `n${toIdx}`, type });
  }

  return { nodes, edges };
}
