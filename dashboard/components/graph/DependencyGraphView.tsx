"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState } from "react";
import { DependencyGraphCanvas } from "@/components/graph/DependencyGraphCanvas";
import { GraphToolbar } from "@/components/graph/GraphToolbar";
import { NodeDetailDrawer } from "@/components/graph/NodeDetailDrawer";
import { adaptGraphResponse } from "@/lib/adapters/graph.adapter";
import { createApiClient } from "@/lib/api-client";
import { queryKeys } from "@/lib/query-keys";
import { useDependencyGraph } from "@/hooks/useDependencyGraph";
import { useRepo } from "@/hooks/useRepo";
import type { ApiEnvelope } from "@/types/api";
import type { GraphEdge, GraphNode, GraphResponse } from "@/types/graph";

export function DependencyGraphView({ scope }: { scope?: string }) {
  const { repoId } = useRepo();
  const queryClient = useQueryClient();
  const { data, isLoading, isError, error } = useDependencyGraph({ scope, depth: 2 });

  // Merged state starts from the base query result and grows via "Expand neighborhood" without
  // ever being replaced by a background refetch of the same scope — only a genuine scope change
  // (navigating to a different node/page) resets it, so expand-driven pan/zoom/selection survive
  // exactly as 06-dashboard.md §6.2 intends.
  const [mergedNodes, setMergedNodes] = useState<GraphNode[]>([]);
  const [mergedEdges, setMergedEdges] = useState<GraphEdge[]>([]);
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<GraphNode | null>(null);
  const [expandingId, setExpandingId] = useState<string | null>(null);
  const lastScopeRef = useRef<string | undefined>(undefined);

  useEffect(() => {
    if (!data) return;
    if (lastScopeRef.current === scope && mergedNodes.length > 0) return;
    lastScopeRef.current = scope;
    setMergedNodes(data.nodes);
    setMergedEdges(data.edges);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data, scope]);

  const searchMatchId = useMemo(() => {
    if (!search.trim()) return undefined;
    const q = search.trim().toLowerCase();
    return mergedNodes.find((n) => n.name.toLowerCase().includes(q))?.id;
  }, [search, mergedNodes]);

  async function handleExpand(nodeId: string) {
    setExpandingId(nodeId);
    try {
      const client = createApiClient(repoId);
      const envelope = await queryClient.fetchQuery({
        queryKey: queryKeys.graph.filtered(repoId, { scope: nodeId, depth: 1 }),
        queryFn: () => client.get<GraphResponse>("/graph", { scope: nodeId, depth: 1 }),
      });
      const neighborhood = adaptGraphResponse((envelope as ApiEnvelope<GraphResponse>).data);

      setMergedNodes((prev) => {
        const existingIds = new Set(prev.map((n) => n.id));
        return [...prev, ...neighborhood.nodes.filter((n) => !existingIds.has(n.id))];
      });
      setMergedEdges((prev) => {
        const existingIds = new Set(prev.map((e) => `${e.fromId}->${e.toId}:${e.type}`));
        return [
          ...prev,
          ...neighborhood.edges.filter((e) => !existingIds.has(`${e.fromId}->${e.toId}:${e.type}`)),
        ];
      });
    } finally {
      setExpandingId(null);
    }
  }

  if (isLoading) {
    return <p className="p-4 text-sm text-muted-foreground">Loading graph…</p>;
  }

  if (isError || !data) {
    return (
      <p className="p-4 text-sm text-red-500">
        Failed to load graph: {error instanceof Error ? error.message : "unknown error"}
      </p>
    );
  }

  return (
    <div className="flex h-full flex-col overflow-hidden rounded-md border border-surface-border">
      <GraphToolbar
        search={search}
        onSearchChange={setSearch}
        nodeCount={mergedNodes.length}
        edgeCount={mergedEdges.length}
        truncated={data.truncated}
        scope={scope}
      />
      <div className="flex flex-1 overflow-hidden">
        <DependencyGraphCanvas
          nodes={mergedNodes}
          edges={mergedEdges}
          onNodeSelect={setSelected}
          focusNodeId={searchMatchId ?? scope}
        />
        {selected && (
          <NodeDetailDrawer
            node={selected}
            onClose={() => setSelected(null)}
            onExpand={() => handleExpand(selected.id)}
            expanding={expandingId === selected.id}
          />
        )}
      </div>
    </div>
  );
}
