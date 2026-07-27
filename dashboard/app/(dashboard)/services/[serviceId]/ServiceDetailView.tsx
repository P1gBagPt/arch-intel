"use client";

import Link from "next/link";
import { useState } from "react";
import { NodeRefList } from "@/components/domain/NodeRefList";
import { MiniDependencyGraph } from "@/components/graph/MiniDependencyGraph";
import { Badge } from "@/components/ui/Badge";
import { useServiceDetail } from "@/hooks/useServices";

const TABS = ["Dependencies", "Callers", "Implements", "Tests"] as const;

export function ServiceDetailView({ serviceId }: { serviceId: string }) {
  const { data: service, isLoading, isError, error } = useServiceDetail(serviceId);
  const [tab, setTab] = useState<(typeof TABS)[number]>("Dependencies");

  if (isLoading) return <p className="text-sm text-muted-foreground">Loading service…</p>;
  if (isError || !service) {
    return (
      <p className="text-sm text-red-500">
        Failed to load service: {error instanceof Error ? error.message : "unknown error"}
      </p>
    );
  }

  const tabData: Record<(typeof TABS)[number], typeof service.dependencies> = {
    Dependencies: service.dependencies,
    Callers: service.callers,
    Implements: service.implements,
    Tests: service.tests,
  };

  return (
    <div className="mx-auto max-w-3xl space-y-4">
      <div className="rounded-md border border-surface-border p-4">
        <div className="flex items-center gap-2">
          <h1 className="text-xl font-semibold">{service.name}</h1>
          <Badge>{service.kind}</Badge>
        </div>
        <div className="mt-2 flex gap-3 text-sm">
          <Link href={`/graph/${encodeURIComponent(service.id)}`} className="text-accent hover:underline">
            Open full graph centered here
          </Link>
          <Link href={`/impact/${encodeURIComponent(service.id)}`} className="text-accent hover:underline">
            Run Impact Analysis
          </Link>
        </div>
      </div>

      <MiniDependencyGraph
        center={{ id: service.id, name: service.name, kind: service.kind }}
        callers={service.callers}
        dependencies={service.dependencies}
      />

      <div className="rounded-md border border-surface-border">
        <div className="flex border-b border-surface-border">
          {TABS.map((t) => (
            <button
              key={t}
              type="button"
              onClick={() => setTab(t)}
              className={
                "flex-1 px-3 py-2 text-sm font-medium " +
                (tab === t ? "border-b-2 border-accent text-accent" : "text-muted-foreground")
              }
            >
              {t} ({tabData[t].length})
            </button>
          ))}
        </div>
        <div className="p-4">
          <NodeRefList items={tabData[tab]} />
        </div>
      </div>
    </div>
  );
}
