import Link from "next/link";
import { Badge } from "@/components/ui/Badge";
import type { GraphNode } from "@/types/graph";

interface NodeDetailDrawerProps {
  node: GraphNode;
  onClose: () => void;
  onExpand?: () => void;
  expanding?: boolean;
}

export function NodeDetailDrawer({ node, onClose, onExpand, expanding }: NodeDetailDrawerProps) {
  return (
    <div className="w-72 shrink-0 border-l border-surface-border p-4">
      <div className="mb-3 flex items-center justify-between">
        <h2 className="text-sm font-semibold">Node details</h2>
        <button type="button" onClick={onClose} className="text-xs text-muted-foreground hover:text-foreground">
          Close
        </button>
      </div>
      <div className="space-y-2">
        <p className="break-words text-sm font-medium">{node.name}</p>
        <Badge>{node.kind}</Badge>
        <div className="flex flex-col gap-2 pt-3 text-sm">
          {onExpand && (
            <button
              type="button"
              onClick={onExpand}
              disabled={expanding}
              className="w-fit rounded-md border border-surface-border px-3 py-1 text-left text-xs font-medium hover:bg-surface disabled:opacity-50"
            >
              {expanding ? "Expanding…" : "Expand neighborhood"}
            </button>
          )}
          <Link href={`/services/${encodeURIComponent(node.id)}`} className="text-accent hover:underline">
            Open in Service Explorer
          </Link>
          <Link href={`/impact/${encodeURIComponent(node.id)}`} className="text-accent hover:underline">
            Run Impact Analysis
          </Link>
        </div>
      </div>
    </div>
  );
}
