import Link from "next/link";
import { Badge } from "@/components/ui/Badge";
import type { AffectedNode } from "@/types/impact";

const RISK_COLOR: Record<string, string> = {
  Low: "text-coupling-stable",
  Medium: "text-coupling-moderate",
  High: "text-coupling-high",
};

export function ImpactSummaryList({ affected }: { affected: AffectedNode[] }) {
  const grouped = new Map<string, AffectedNode[]>();
  for (const node of affected) {
    const list = grouped.get(node.kind) ?? [];
    list.push(node);
    grouped.set(node.kind, list);
  }

  if (affected.length === 0) {
    return <p className="py-4 text-sm text-muted-foreground">No affected components found.</p>;
  }

  return (
    <div className="space-y-4">
      {[...grouped.entries()].map(([kind, items]) => (
        <div key={kind}>
          <h3 className="mb-1 text-sm font-semibold">
            ✓ {kind} <span className="font-normal text-muted-foreground">({items.length})</span>
          </h3>
          <ul className="divide-y divide-surface-border rounded-md border border-surface-border">
            {items.map((node) => (
              <li key={node.id} className="flex items-center justify-between px-3 py-1.5 text-sm">
                <Link href={`/graph/${encodeURIComponent(node.id)}`} className="hover:text-accent">
                  {node.name}
                </Link>
                <div className="flex items-center gap-2">
                  <Badge>{node.relation}</Badge>
                  <span className={RISK_COLOR[node.riskLevel] ?? ""}>{node.riskLevel}</span>
                </div>
              </li>
            ))}
          </ul>
        </div>
      ))}
    </div>
  );
}
