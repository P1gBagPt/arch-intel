"use client";

import Graph from "graphology";
import Sigma from "sigma";
import { useEffect, useRef } from "react";
import type { GraphRendererHandle, GraphRendererProps } from "@/components/graph/graph-renderer-types";
import { nodeKindColor } from "@/lib/constants/relationship-styles";

const DIMMED_COLOR = "#3f3f46";
const TARGET_COLOR = "#2563eb";
const RISK_COLORS: Record<string, string> = {
  Low: "#16a34a",
  Medium: "#ca8a04",
  High: "#dc2626",
};

// Deterministic grid placement, not a force layout — Sigma is chosen here purely for its WebGL
// rendering scale (06-dashboard.md §4.2), not for layout quality. A real force-directed layout
// for graphs this large (graphology-layout-forceatlas2) is a reasonable follow-up, not required
// to prove the rendering approach out.
function assignGridPositions(graph: Graph) {
  const nodeIds = graph.nodes();
  const columns = Math.ceil(Math.sqrt(nodeIds.length));
  const spacing = 12;
  nodeIds.forEach((id, i) => {
    graph.setNodeAttribute(id, "x", (i % columns) * spacing);
    graph.setNodeAttribute(id, "y", Math.floor(i / columns) * spacing);
  });
}

function measurePanZoomStress(sigma: Sigma, durationMs: number, onDone: (fps: number) => void) {
  const camera = sigma.getCamera();
  let frames = 0;
  let angle = 0;
  const start = performance.now();
  const baseState = camera.getState();

  function tick(now: number) {
    frames++;
    angle += 0.05;
    camera.setState({
      ...baseState,
      ratio: baseState.ratio * (1 + 0.3 * Math.sin(angle)),
      angle: angle * 0.1,
    });
    if (now - start < durationMs) {
      requestAnimationFrame(tick);
    } else {
      onDone(Math.round((frames / (now - start)) * 1000));
    }
  }

  requestAnimationFrame(tick);
}

export function SigmaGraphRenderer({
  nodes,
  edges,
  onNodeSelect,
  focusNodeId,
  className,
  impactRootId,
  impactRiskByNodeId,
  onReady,
}: GraphRendererProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const sigmaRef = useRef<Sigma | null>(null);
  // Read by the node/edge reducers on every frame — refs (not state) so updating focus/impact
  // props doesn't require tearing down and rebuilding the whole Sigma instance.
  const focusRef = useRef<string | undefined>(undefined);
  const impactRootRef = useRef<string | undefined>(undefined);
  const impactRiskRef = useRef<Record<string, string>>({});

  useEffect(() => {
    focusRef.current = focusNodeId;
    impactRootRef.current = impactRootId;
    impactRiskRef.current = impactRiskByNodeId ?? {};
    sigmaRef.current?.refresh();
  }, [focusNodeId, impactRootId, impactRiskByNodeId]);

  useEffect(() => {
    if (!containerRef.current) return;

    const graph = new Graph();
    const nodeIds = new Set(nodes.map((n) => n.id));
    for (const node of nodes) {
      graph.addNode(node.id, { label: node.name, kind: node.kind, color: nodeKindColor(node.kind), size: 3 });
    }
    for (const edge of edges) {
      if (!nodeIds.has(edge.fromId) || !nodeIds.has(edge.toId) || edge.fromId === edge.toId) continue;
      const key = `${edge.fromId}->${edge.toId}`;
      if (!graph.hasEdge(key) && !graph.hasEdge(edge.fromId, edge.toId)) {
        graph.addEdgeWithKey(key, edge.fromId, edge.toId, { color: "#52525b", size: 0.5 });
      }
    }
    assignGridPositions(graph);

    const layoutStart = performance.now();
    const sigma = new Sigma(graph, containerRef.current, {
      renderLabels: nodes.length < 200,
      nodeReducer: (node, data) => {
        const impactRoot = impactRootRef.current;
        const risk = impactRiskRef.current;
        if (impactRoot) {
          if (node === impactRoot) return { ...data, color: TARGET_COLOR, size: data.size * 2, zIndex: 2 };
          if (risk[node]) return { ...data, color: RISK_COLORS[risk[node]] ?? data.color, zIndex: 1 };
          return { ...data, color: DIMMED_COLOR, size: data.size * 0.6 };
        }
        if (focusRef.current) {
          return node === focusRef.current
            ? { ...data, color: TARGET_COLOR, size: data.size * 2.5, zIndex: 2 }
            : { ...data, color: DIMMED_COLOR };
        }
        return data;
      },
      edgeReducer: (edge, data) => {
        const impactRoot = impactRootRef.current;
        if (!impactRoot) return data;
        const [source, target] = graph.extremities(edge);
        const risk = impactRiskRef.current;
        const relevant = source === impactRoot || target === impactRoot || risk[source] || risk[target];
        return relevant ? data : { ...data, color: "#27272a", hidden: true };
      },
    });

    sigma.on("clickNode", ({ node }) => {
      const attrs = graph.getNodeAttributes(node);
      onNodeSelect?.({ id: node, kind: attrs.kind, name: attrs.label });
    });
    sigma.on("clickStage", () => onNodeSelect?.(null));

    sigmaRef.current = sigma;

    const handle: GraphRendererHandle = {
      rendererName: "sigma",
      nodeCount: graph.order,
      edgeCount: graph.size,
      layoutMs: Math.round(performance.now() - layoutStart),
      measurePanZoomStress: (durationMs, onDone) => measurePanZoomStress(sigma, durationMs, onDone),
    };
    onReady?.(handle);

    return () => {
      sigma.kill();
      sigmaRef.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [nodes, edges]);

  useEffect(() => {
    const sigma = sigmaRef.current;
    if (!sigma || !focusNodeId) return;
    const display = sigma.getNodeDisplayData(focusNodeId);
    if (!display) return;
    sigma.getCamera().animate({ x: display.x, y: display.y, ratio: 0.15 }, { duration: 300 });
  }, [focusNodeId]);

  useEffect(() => {
    const sigma = sigmaRef.current;
    if (!sigma || !impactRootId) return;
    const display = sigma.getNodeDisplayData(impactRootId);
    if (!display) return;
    sigma.getCamera().animate({ x: display.x, y: display.y, ratio: 0.3 }, { duration: 300 });
  }, [impactRootId]);

  return <div ref={containerRef} className={className ?? "h-full w-full"} />;
}
