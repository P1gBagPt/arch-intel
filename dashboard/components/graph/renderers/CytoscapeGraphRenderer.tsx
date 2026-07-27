"use client";

import cytoscape, { type Core, type ElementDefinition, type StylesheetJson } from "cytoscape";
import { useEffect, useRef } from "react";
import type { GraphRendererHandle, GraphRendererProps } from "@/components/graph/graph-renderer-types";
import { nodeKindColor, relationshipStyle } from "@/lib/constants/relationship-styles";

// 06-dashboard.md §11's load test found default `cose` (force-directed, ~O(n²) per iteration)
// hangs the render thread badly enough to make the tab unresponsive well before the plan's
// estimated 2,000-5,000 node degradation point — closer to ~1,000. `grid` is O(n) and gives up
// the organic layout; this renderer is only ever mounted below the Sigma cutover (see
// DependencyGraphCanvas), but keeps this fallback for the upper end of its own range.
const GRID_LAYOUT_THRESHOLD = 500;

function layoutOptionsFor(nodeCount: number) {
  return nodeCount >= GRID_LAYOUT_THRESHOLD
    ? ({ name: "grid", fit: true, padding: 40 } as const)
    : ({ name: "cose", animate: false, fit: true, padding: 40 } as const);
}

function toElements(nodes: GraphRendererProps["nodes"], edges: GraphRendererProps["edges"]): ElementDefinition[] {
  const nodeIds = new Set(nodes.map((n) => n.id));
  return [
    ...nodes.map((n) => ({
      data: { id: n.id, label: n.name, kind: n.kind },
    })),
    ...edges
      .filter((e) => nodeIds.has(e.fromId) && nodeIds.has(e.toId))
      .map((e) => ({
        data: {
          id: `${e.fromId}->${e.toId}:${e.type}`,
          source: e.fromId,
          target: e.toId,
          type: e.type,
        },
      })),
  ];
}

// cytoscape's TS types don't model `"data(...)"` mapper strings for every property
// (line-style included), even though they're valid at runtime — see cytoscape/cytoscape.js#3168.
const stylesheet = [
  {
    selector: "node",
    style: {
      "background-color": "data(color)",
      label: "data(label)",
      "font-size": 9,
      color: "#e5e7eb",
      "text-outline-width": 2,
      "text-outline-color": "data(color)",
      width: 24,
      height: 24,
    },
  },
  {
    selector: "edge",
    style: {
      width: 1.5,
      "line-color": "data(color)",
      "target-arrow-color": "data(color)",
      "target-arrow-shape": "triangle",
      "curve-style": "bezier",
      "line-style": "data(lineStyle)",
      opacity: 0.7,
    },
  },
  {
    selector: ".dimmed",
    style: { opacity: 0.15 },
  },
  {
    selector: ".highlighted",
    style: { "border-width": 3, "border-color": "#f59e0b" },
  },
  {
    selector: ".impact-target",
    style: { "border-width": 4, "border-color": "#2563eb", "border-style": "double", width: 32, height: 32 },
  },
  {
    selector: ".risk-low",
    style: { "border-width": 3, "border-color": "#16a34a" },
  },
  {
    selector: ".risk-medium",
    style: { "border-width": 3, "border-color": "#ca8a04" },
  },
  {
    selector: ".risk-high",
    style: { "border-width": 3, "border-color": "#dc2626" },
  },
] as StylesheetJson;

function riskClass(riskLevel: string | undefined): string {
  switch (riskLevel) {
    case "Low":
      return "risk-low";
    case "Medium":
      return "risk-medium";
    case "High":
      return "risk-high";
    default:
      return "";
  }
}

function measurePanZoomStress(cy: Core, durationMs: number, onDone: (fps: number) => void) {
  let frames = 0;
  let angle = 0;
  const start = performance.now();

  function tick(now: number) {
    frames++;
    angle += 0.05;
    cy.zoom(1 + 0.3 * Math.sin(angle));
    cy.pan({ x: 400 + 50 * Math.cos(angle), y: 300 + 50 * Math.sin(angle) });
    if (now - start < durationMs) {
      requestAnimationFrame(tick);
    } else {
      onDone(Math.round((frames / (now - start)) * 1000));
    }
  }

  requestAnimationFrame(tick);
}

export function CytoscapeGraphRenderer({
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
  const cyRef = useRef<Core | null>(null);
  const prevIdsRef = useRef<Set<string>>(new Set());

  // Destroys whatever cy instance is current at unmount time — kept separate from the data
  // effect below so the incremental-add branch (which never recreates cy) doesn't need to
  // return its own no-op cleanup.
  useEffect(() => {
    return () => cyRef.current?.destroy();
  }, []);

  useEffect(() => {
    if (!containerRef.current) return;

    const elements = toElements(nodes, edges).map((el) => {
      if ("source" in el.data) {
        const style = relationshipStyle((el.data as { type: string }).type);
        return { ...el, data: { ...el.data, color: style.color, lineStyle: style.lineStyle } };
      }
      return {
        ...el,
        data: { ...el.data, color: nodeKindColor((el.data as { kind: string }).kind) },
      };
    });

    const newIds = new Set(elements.map((el) => el.data.id as string));
    const cy = cyRef.current;

    // 06-dashboard.md §6.2: expanding a node grows the existing dataset without discarding
    // it — merge-patch with an incremental cy.add(), not a full teardown/rebuild, so pan/zoom/
    // selection survive. Anything that ISN'T a pure superset (a genuinely different scope/search
    // result) falls through to the full rebuild below, where losing camera state is expected.
    const isPureAddition =
      !!cy && newIds.size > prevIdsRef.current.size && [...prevIdsRef.current].every((id) => newIds.has(id));

    if (cy && isPureAddition) {
      const toAdd = elements.filter((el) => !prevIdsRef.current.has(el.data.id as string));
      const added = cy.add(toAdd);
      const addedNodes = added.filter("node");
      if (addedNodes.length > 0) {
        addedNodes.layout({ name: "cose", animate: false, fit: false, randomize: false }).run();
      }
      prevIdsRef.current = newIds;
      return;
    }

    cy?.destroy();

    // No inline `layout` option here — cytoscape runs an animate:false layout synchronously
    // during construction, which would fire 'layoutstop' before onReady's caller ever gets a
    // chance to attach a listener for it. Running layout as an explicit .run() after attaching
    // that listener lets the layoutMs timing below actually capture the real duration.
    const nextCy = cytoscape({
      container: containerRef.current,
      elements,
      style: stylesheet,
      minZoom: 0.1,
      maxZoom: 4,
    });

    nextCy.on("tap", "node", (evt) => {
      const data = evt.target.data();
      onNodeSelect?.({ id: data.id, kind: data.kind, name: data.label });
    });

    nextCy.on("tap", (evt) => {
      if (evt.target === nextCy) onNodeSelect?.(null);
    });

    cyRef.current = nextCy;
    prevIdsRef.current = newIds;

    // e2e test hook only (tests/e2e/fixtures/api.ts's specs drive real node clicks through this
    // rather than guessing pixel coordinates over a non-deterministic cose/grid layout — it
    // dispatches cy's own "tap" event, so it exercises the exact same onNodeSelect path a real
    // mouse click would).
    if (process.env.NODE_ENV !== "production") {
      (window as unknown as { __cyInstance?: Core }).__cyInstance = nextCy;
    }

    const layoutStart = performance.now();
    nextCy.one("layoutstop", () => {
      const handle: GraphRendererHandle = {
        rendererName: "cytoscape",
        nodeCount: nextCy.nodes().length,
        edgeCount: nextCy.edges().length,
        layoutMs: Math.round(performance.now() - layoutStart),
        measurePanZoomStress: (durationMs, onDone) => measurePanZoomStress(nextCy, durationMs, onDone),
      };
      onReady?.(handle);
    });
    nextCy.layout(layoutOptionsFor(nodes.length)).run();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [nodes, edges]);

  useEffect(() => {
    const cy = cyRef.current;
    if (!cy || !focusNodeId || impactRootId) return;
    const target = cy.getElementById(focusNodeId);
    if (target.length === 0) return;
    cy.elements().removeClass("dimmed highlighted");
    cy.elements().not(target.closedNeighborhood()).addClass("dimmed");
    target.addClass("highlighted");
    cy.animate({ center: { eles: target }, zoom: 1.5 }, { duration: 300 });
  }, [focusNodeId, impactRootId]);

  useEffect(() => {
    const cy = cyRef.current;
    if (!cy || !impactRootId) return;

    cy.elements().removeClass("dimmed highlighted impact-target risk-low risk-medium risk-high");

    const root = cy.getElementById(impactRootId);
    if (root.length === 0) return;

    const affectedIds = Object.keys(impactRiskByNodeId ?? {});
    const affectedNodes = cy.nodes().filter((n) => affectedIds.includes(n.id()));
    const keepNodes = root.union(affectedNodes);
    const keepEdges = keepNodes.edgesWith(keepNodes);
    const keep = keepNodes.union(keepEdges);

    cy.elements().not(keep).addClass("dimmed");
    root.addClass("impact-target");
    for (const [nodeId, risk] of Object.entries(impactRiskByNodeId ?? {})) {
      const cls = riskClass(risk);
      if (cls) cy.getElementById(nodeId).addClass(cls);
    }

    cy.animate({ fit: { eles: keep, padding: 60 } }, { duration: 300 });
  }, [impactRootId, impactRiskByNodeId, nodes, edges]);

  return <div ref={containerRef} className={className ?? "h-full w-full"} />;
}
