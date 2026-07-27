"use client";

import { useMemo } from "react";
import { DependencyGraphCanvas } from "@/components/graph/DependencyGraphCanvas";
import { useDependencyGraph } from "@/hooks/useDependencyGraph";
import { useImpactAnalysis } from "@/hooks/useImpactAnalysis";

// Cytoscape-backed for now (06-dashboard.md §4.4 reuses <DependencyGraphCanvas />) — the
// size-based React Flow swap for small blast radii (<~30 nodes) is deferred; Cytoscape renders
// small graphs fine, it's just not the crisp curated layout the doc envisions for that case.
const LEGEND = [
  { label: "Target", className: "border-[3px] border-double border-blue-600" },
  { label: "Low risk", className: "border-[3px] border-coupling-stable" },
  { label: "Medium risk", className: "border-[3px] border-coupling-moderate" },
  { label: "High risk", className: "border-[3px] border-coupling-high" },
];

export function ImpactGraphHighlight({ targetId, maxDepth }: { targetId: string; maxDepth?: number }) {
  const graph = useDependencyGraph({ scope: targetId, depth: maxDepth ?? 5 });
  const impact = useImpactAnalysis(targetId, maxDepth);

  const riskByNodeId = useMemo(() => {
    if (!impact.data) return {};
    return Object.fromEntries(impact.data.affected.map((n) => [n.id, n.riskLevel]));
  }, [impact.data]);

  if (graph.isLoading || impact.isLoading) {
    return <p className="p-4 text-sm text-muted-foreground">Loading graph highlight…</p>;
  }

  if (graph.isError || !graph.data) {
    return (
      <p className="p-4 text-sm text-red-500">
        Failed to load graph highlight:{" "}
        {graph.error instanceof Error ? graph.error.message : "unknown error"}
      </p>
    );
  }

  return (
    <div className="flex h-96 flex-col overflow-hidden rounded-md border border-surface-border">
      <div className="flex items-center gap-4 border-b border-surface-border px-4 py-2 text-xs text-muted-foreground">
        {LEGEND.map((item) => (
          <span key={item.label} className="flex items-center gap-1.5">
            <span className={`h-3 w-3 rounded-full ${item.className}`} />
            {item.label}
          </span>
        ))}
      </div>
      <DependencyGraphCanvas
        nodes={graph.data.nodes}
        edges={graph.data.edges}
        impactRootId={targetId}
        impactRiskByNodeId={riskByNodeId}
      />
    </div>
  );
}
