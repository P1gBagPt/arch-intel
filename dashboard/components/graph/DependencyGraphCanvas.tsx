"use client";

import dynamic from "next/dynamic";
import type { GraphRendererProps } from "@/components/graph/graph-renderer-types";
import { CytoscapeGraphRenderer } from "@/components/graph/renderers/CytoscapeGraphRenderer";

// sigma's module-level code references WebGL2RenderingContext, which doesn't exist during
// Next.js's SSR pass — eagerly importing it (even from a "use client" file, since App Router
// still server-renders client components once for the initial HTML) crashed every page that
// touched <DependencyGraphCanvas />, Cytoscape-only or not. ssr:false defers the import to the
// browser, where it's only ever needed past the Sigma cutover below anyway.
const SigmaGraphRenderer = dynamic(
  () => import("@/components/graph/renderers/SigmaGraphRenderer").then((m) => m.SigmaGraphRenderer),
  { ssr: false },
);

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
