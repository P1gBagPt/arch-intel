// Edge color/style per relationship type (06-dashboard.md §4.2, §5.2) — shared so Dependency
// Graph, Service Explorer, and Impact Analysis draw the same relationship consistently.
export const RELATIONSHIP_STYLES: Record<string, { color: string; lineStyle: "solid" | "dashed" | "dotted" }> = {
  References: { color: "#64748b", lineStyle: "solid" },
  Calls: { color: "#2563eb", lineStyle: "solid" },
  Implements: { color: "#7c3aed", lineStyle: "dashed" },
  Inherits: { color: "#a855f7", lineStyle: "dashed" },
  Injects: { color: "#059669", lineStyle: "dotted" },
  Uses: { color: "#059669", lineStyle: "dotted" },
  Publishes: { color: "#d97706", lineStyle: "solid" },
  Consumes: { color: "#d97706", lineStyle: "dashed" },
  Owns: { color: "#0891b2", lineStyle: "solid" },
  Contains: { color: "#94a3b8", lineStyle: "dotted" },
};

export const DEFAULT_RELATIONSHIP_STYLE = { color: "#94a3b8", lineStyle: "solid" as const };

export function relationshipStyle(type: string) {
  return RELATIONSHIP_STYLES[type] ?? DEFAULT_RELATIONSHIP_STYLE;
}

const NODE_KIND_COLORS: Record<string, string> = {
  Project: "#2563eb",
  Class: "#0891b2",
  Interface: "#7c3aed",
  Method: "#64748b",
  TestClass: "#d97706",
  TestMethod: "#d97706",
  Service: "#059669",
};

export function nodeKindColor(kind: string) {
  return NODE_KIND_COLORS[kind] ?? "#6b7280";
}
