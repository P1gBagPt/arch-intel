"use client";

import type { GraphRendererProps } from "@/components/graph/graph-renderer-types";
import { CytoscapeGraphRenderer } from "@/components/graph/renderers/CytoscapeGraphRenderer";
import { SigmaGraphRenderer } from "@/components/graph/renderers/SigmaGraphRenderer";

// 06-dashboard.md §4.2/§11: Cytoscape is the default renderer (mature filter/expand interaction
// support — compound nodes, plugin ecosystem), but the load test showed its canvas rendering
// degrade sharply well below the plan's estimated 3,000-5,000 node ceiling. Sigma (WebGL) is the
// designated escape hatch above that; both implementations share the same GraphRendererProps
// contract, so nothing upstream (toolbar, filter panel, node drawer) needs to know which one is
// mounted.
const SIGMA_CUTOVER_NODE_COUNT = 3000;

export function DependencyGraphCanvas(props: GraphRendererProps) {
  const Renderer = props.nodes.length >= SIGMA_CUTOVER_NODE_COUNT ? SigmaGraphRenderer : CytoscapeGraphRenderer;
  return <Renderer {...props} />;
}
