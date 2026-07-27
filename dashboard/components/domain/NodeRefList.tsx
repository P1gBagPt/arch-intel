import Link from "next/link";
import { Badge } from "@/components/ui/Badge";
import type { NodeRef } from "@/types/service";

export function NodeRefList({ items }: { items: NodeRef[] }) {
  if (items.length === 0) {
    return <p className="py-4 text-sm text-muted-foreground">None</p>;
  }

  return (
    <ul className="divide-y divide-surface-border">
      {items.map((item) => (
        <li key={item.id} className="flex items-center justify-between py-2">
          <Link href={`/services/${encodeURIComponent(item.id)}`} className="text-sm hover:text-accent">
            {item.name}
          </Link>
          <div className="flex items-center gap-2">
            {item.relation && <Badge>{item.relation}</Badge>}
            <Badge>{item.kind}</Badge>
          </div>
        </li>
      ))}
    </ul>
  );
}
