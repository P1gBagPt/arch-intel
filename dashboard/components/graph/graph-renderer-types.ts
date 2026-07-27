import type { GraphEdge, GraphNode } from "@/types/graph";

// Renderer-agnostic contract (06-dashboard.md §4.2/§11): every prop a GraphRenderer
// implementation needs, independent of whether it's backed by Cytoscape or Sigma. Consumers
// (DependencyGraphView, ImpactGraphHighlight, the load-test harness) target this interface via
// <DependencyGraphCanvas />, which picks the concrete renderer by node count — swapping or
// adding a renderer never touches the surrounding toolbar/filter/drawer components.
export interface GraphRendererProps {
  nodes: GraphNode[];
  edges: GraphEdge[];
  onNodeSelect?: (node: GraphNode | null) => void;
  focusNodeId?: string;
  className?: string;
  // Impact Analysis overlay (§4.4): dims everything outside target + affected, rings the
  // target node, and colors each affected node's border by its risk level.
  impactRootId?: string;
  impactRiskByNodeId?: Record<string, string>;
  onReady?: (handle: GraphRendererHandle) => void;
}

// What the §11 load-test harness (and any future instrumentation) can ask of a renderer without
// reaching into its underlying library — each renderer times its own initial layout internally
// and implements the pan/zoom stress loop using whatever camera/viewport API it has.
export interface GraphRendererHandle {
  rendererName: "cytoscape" | "sigma";
  nodeCount: number;
  edgeCount: number;
  layoutMs: number;
  measurePanZoomStress: (durationMs: number, onDone: (fps: number) => void) => void;
}
