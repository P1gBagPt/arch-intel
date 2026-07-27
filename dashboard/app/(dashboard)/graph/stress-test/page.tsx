"use client";

import { useState } from "react";
import { DependencyGraphCanvas } from "@/components/graph/DependencyGraphCanvas";
import type { GraphRendererHandle } from "@/components/graph/graph-renderer-types";
import { generateSyntheticGraph } from "@/lib/testing/synthetic-graph";
import type { GraphEdge, GraphNode } from "@/types/graph";

// 06-dashboard.md §11: "large graph rendering performance is the single biggest technical risk"
// — load-test with a synthetic graph at 1k/5k/10k/20k nodes before Phase 2 sign-off. Not linked
// from nav; a dev-only harness for exercising <DependencyGraphCanvas /> at real-world scale since
// no scanned solution in this repo comes close. Goes through the renderer-agnostic
// GraphRendererHandle contract so it exercises whichever renderer (Cytoscape or Sigma) the
// dispatcher actually picks for a given node count, same as production views.
const PRESETS = [1000, 5000, 10000, 20000];
const MEASURE_DURATION_MS = 2000;

interface Metrics {
  renderer: string;
  nodeCount: number;
  edgeCount: number;
  layoutMs: number;
  fps: number | null;
}

export default function GraphStressTestPage() {
  const [graph, setGraph] = useState<{ nodes: GraphNode[]; edges: GraphEdge[] } | null>(null);
  const [metrics, setMetrics] = useState<Metrics | null>(null);
  const [running, setRunning] = useState(false);
  const [selected, setSelected] = useState<GraphNode | null>(null);

  function runTest(nodeCount: number) {
    setRunning(true);
    setMetrics(null);
    setGraph(generateSyntheticGraph(nodeCount));
  }

  function handleReady(handle: GraphRendererHandle) {
    handle.measurePanZoomStress(MEASURE_DURATION_MS, (fps) => {
      setMetrics({
        renderer: handle.rendererName,
        nodeCount: handle.nodeCount,
        edgeCount: handle.edgeCount,
        layoutMs: handle.layoutMs,
        fps,
      });
      setRunning(false);
    });
  }

  return (
    <div className="flex h-full flex-col gap-4">
      <div className="rounded-md border border-surface-border p-4">
        <h1 className="text-lg font-semibold">Dependency Graph load test</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Renders a synthetic graph through the same <code>DependencyGraphCanvas</code> dispatcher
          the Dependency Graph view uses (Cytoscape below 3,000 nodes, Sigma at/above), and
          measures initial layout time plus sustained pan/zoom FPS over a{" "}
          {MEASURE_DURATION_MS / 1000}s window.
        </p>
        <div className="mt-3 flex flex-wrap items-center gap-2">
          {PRESETS.map((count) => (
            <button
              key={count}
              type="button"
              disabled={running}
              onClick={() => runTest(count)}
              className="rounded-md border border-surface-border px-3 py-1.5 text-sm font-medium hover:bg-surface disabled:opacity-50"
            >
              {count.toLocaleString()} nodes
            </button>
          ))}
          {running && <span className="text-sm text-muted-foreground">Running…</span>}
        </div>
        {metrics && (
          <dl className="mt-4 grid grid-cols-2 gap-x-6 gap-y-1 text-sm sm:grid-cols-5">
            <div>
              <dt className="text-muted-foreground">Renderer</dt>
              <dd className="font-medium capitalize">{metrics.renderer}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">Nodes</dt>
              <dd className="font-medium">{metrics.nodeCount.toLocaleString()}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">Edges</dt>
              <dd className="font-medium">{metrics.edgeCount.toLocaleString()}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">Layout time</dt>
              <dd className="font-medium">{metrics.layoutMs} ms</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">Pan/zoom FPS</dt>
              <dd className={`font-medium ${metrics.fps !== null && metrics.fps < 20 ? "text-coupling-high" : ""}`}>
                {metrics.fps ?? "—"}
              </dd>
            </div>
          </dl>
        )}
        {selected && (
          <p className="mt-2 text-sm text-muted-foreground">
            Selected: <span className="font-medium text-foreground">{selected.name}</span> ({selected.kind})
          </p>
        )}
      </div>
      <div className="flex-1 overflow-hidden rounded-md border border-surface-border">
        {graph ? (
          <DependencyGraphCanvas
            nodes={graph.nodes}
            edges={graph.edges}
            onReady={handleReady}
            onNodeSelect={setSelected}
          />
        ) : (
          <p className="p-4 text-sm text-muted-foreground">Pick a node count above to generate a graph.</p>
        )}
      </div>
    </div>
  );
}
