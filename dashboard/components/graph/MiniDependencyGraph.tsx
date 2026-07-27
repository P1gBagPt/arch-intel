"use client";

import "reactflow/dist/style.css";
import ReactFlow, { Background, Controls, type Edge, type Node } from "reactflow";
import { useMemo } from "react";
import { useRouter } from "next/navigation";
import { MiniGraphNode, type MiniGraphNodeData } from "@/components/graph/MiniGraphNode";
import type { NodeRef } from "@/types/service";

const nodeTypes = { miniGraphNode: MiniGraphNode };

const COLUMN_X = { callers: 0, center: 280, dependencies: 560 };
const ROW_HEIGHT = 64;

interface MiniDependencyGraphProps {
  center: { id: string; name: string; kind: string };
  callers: NodeRef[];
  dependencies: NodeRef[];
}

// React Flow, not Cytoscape (06-dashboard.md §4.3): this view is always small — one service plus
// its direct one-hop neighbors — and benefits from styled custom node cards and simple manual
// columnar layout rather than a force-directed algorithm built for hundreds of nodes.
export function MiniDependencyGraph({ center, callers, dependencies }: MiniDependencyGraphProps) {
  const router = useRouter();

  const { nodes, edges } = useMemo(() => {
    const nodes: Node<MiniGraphNodeData>[] = [
      {
        id: center.id,
        type: "miniGraphNode",
        position: { x: COLUMN_X.center, y: (Math.max(callers.length, dependencies.length) * ROW_HEIGHT) / 2 },
        data: { label: center.name, kind: center.kind, isCenter: true },
      },
      ...callers.map((c, i) => ({
        id: c.id,
        type: "miniGraphNode",
        position: { x: COLUMN_X.callers, y: i * ROW_HEIGHT },
        data: { label: c.name, kind: c.kind },
      })),
      ...dependencies.map((d, i) => ({
        id: d.id,
        type: "miniGraphNode",
        position: { x: COLUMN_X.dependencies, y: i * ROW_HEIGHT },
        data: { label: d.name, kind: d.kind },
      })),
    ];

    const edges: Edge[] = [
      ...callers.map((c) => ({
        id: `${c.id}->${center.id}`,
        source: c.id,
        target: center.id,
        label: c.relation ?? undefined,
        style: { stroke: "#64748b" },
      })),
      ...dependencies.map((d) => ({
        id: `${center.id}->${d.id}`,
        source: center.id,
        target: d.id,
        label: d.relation ?? undefined,
        style: { stroke: "#64748b" },
      })),
    ];

    return { nodes, edges };
  }, [center, callers, dependencies]);

  if (callers.length === 0 && dependencies.length === 0) {
    return <p className="p-4 text-sm text-muted-foreground">No direct callers or dependencies to visualize.</p>;
  }

  return (
    <div className="h-64 rounded-md border border-surface-border">
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        onNodeClick={(_, node) => router.push(`/services/${encodeURIComponent(node.id)}`)}
        fitView
        proOptions={{ hideAttribution: true }}
        nodesDraggable={false}
        nodesConnectable={false}
      >
        <Background />
        <Controls showInteractive={false} />
      </ReactFlow>
    </div>
  );
}
