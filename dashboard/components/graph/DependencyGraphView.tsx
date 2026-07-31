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

  // Landing on /graph with no scope used to eagerly fetch the ENTIRE graph (GraphScopeResolver
  // treats "no scope" as "whole graph", capped at 100k nodes server-side — no cap at all for a
  // real mid-size solution). Against a real ~5,000-node repo this took 50+s server-side alone and
  // then hung the renderer client-side — exactly the risk 06-dashboard.md §11 flagged and was
  // supposed to be mitigated by defaulting to a "top-level projects only, expand-on-demand" view,
  // which never actually got wired up (only ever exercised against small fixtures/demo repos
  // until now). Defaults to Project-kind-only here; deep-linked scopes (a specific project/node,
  // e.g. from Repository Explorer or Impact Analysis) are unaffected and always show full detail.
  const [showFullGraph, setShowFullGraph] = useState(false);
  const isProjectMapView = !scope && !showFullGraph;
  const { data, isLoading, isError, error } = useDependencyGraph({
    scope,
    depth: 2,
    kinds: isProjectMapView ? ["Project"] : undefined,
  });

  // Merged state starts from the base query result and grows via "Expand neighborhood" without
  // ever being replaced by a background refetch of the same scope — only a genuine scope change
  // (navigating to a different node/page, or toggling the project-map/full-graph view) resets it,
  // so expand-driven pan/zoom/selection survive exactly as 06-dashboard.md §6.2 intends.
  const [mergedNodes, setMergedNodes] = useState<GraphNode[]>([]);
  const [mergedEdges, setMergedEdges] = useState<GraphEdge[]>([]);
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<GraphNode | null>(null);
  const [expandingId, setExpandingId] = useState<string | null>(null);
  const viewKey = `${scope ?? ""}:${isProjectMapView}`;
  const lastViewKeyRef = useRef<string | undefined>(undefined);

  useEffect(() => {
    if (!data) return;
    if (lastViewKeyRef.current === viewKey && mergedNodes.length > 0) return;
    lastViewKeyRef.current = viewKey;
    setMergedNodes(data.nodes);
    setMergedEdges(data.edges);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data, viewKey]);

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
        isProjectMapView={isProjectMapView}
        onLoadFullGraph={() => {
          if (
            window.confirm(
              "Loading the full graph can be slow and may be hard to navigate on a large repository. Continue?",
            )
          ) {
            setShowFullGraph(true);
          }
        }}
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
