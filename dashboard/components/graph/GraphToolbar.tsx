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
}

export function GraphToolbar({ search, onSearchChange, nodeCount, edgeCount, truncated, scope }: GraphToolbarProps) {
  return (
    <div className="flex items-center gap-4 border-b border-surface-border px-4 py-2">
      <SearchInput
        value={search}
        onChange={onSearchChange}
        placeholder="Search nodes…"
        className="max-w-xs"
      />
      <span className="text-xs text-muted-foreground">
        {nodeCount} nodes · {edgeCount} edges
        {truncated && <span className="ml-1 text-amber-500">(truncated — narrow the scope)</span>}
      </span>
      <div className="ml-auto">
        <ExportMermaidButton scope={scope} depth={2} />
      </div>
    </div>
  );
}
