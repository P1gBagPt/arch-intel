"use client";

import { ExportMermaidButton } from "@/components/graph/ExportMermaidButton";
import { SearchInput } from "@/components/ui/SearchInput";

interface GraphToolbarProps {
  search: string;
  onSearchChange: (value: string) => void;
  nodeCount: number;
  edgeCount: number;
  truncated: boolean;
  scope?: string;
  isProjectMapView?: boolean;
  onLoadFullGraph?: () => void;
}

export function GraphToolbar({
  search,
  onSearchChange,
  nodeCount,
  edgeCount,
  truncated,
  scope,
  isProjectMapView,
  onLoadFullGraph,
}: GraphToolbarProps) {
  return (
    <div className="flex items-center gap-4 border-b border-surface-border px-4 py-2">
      <SearchInput
        value={search}
        onChange={onSearchChange}
        placeholder="Search nodes…"
        className="max-w-xs"
      />
      <span className="text-xs text-muted-foreground">
        {isProjectMapView ? `Project map — ${nodeCount} projects · ${edgeCount} references` : `${nodeCount} nodes · ${edgeCount} edges`}
        {truncated && <span className="ml-1 text-amber-500">(truncated — narrow the scope)</span>}
      </span>
      {isProjectMapView && onLoadFullGraph && (
        <button
          type="button"
          onClick={onLoadFullGraph}
          className="rounded-md border border-surface-border px-2 py-1 text-xs text-muted-foreground hover:bg-surface"
        >
          Load full graph
        </button>
      )}
      <div className="ml-auto">
        <ExportMermaidButton scope={scope} depth={2} />
      </div>
    </div>
  );
}
