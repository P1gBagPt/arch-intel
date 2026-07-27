"use client";

import Link from "next/link";
import { ImpactSummaryList } from "@/components/domain/ImpactSummaryList";
import { ExportMermaidButton } from "@/components/graph/ExportMermaidButton";
import { ImpactGraphHighlight } from "@/components/graph/ImpactGraphHighlight";
import { useImpactAnalysis } from "@/hooks/useImpactAnalysis";

export function ImpactResultView({ targetId }: { targetId: string }) {
  const { data, isLoading, isError, error } = useImpactAnalysis(targetId);

  if (isLoading) return <p className="text-sm text-muted-foreground">Analyzing impact…</p>;
  if (isError || !data) {
    return (
      <p className="text-sm text-red-500">
        Failed to analyze impact: {error instanceof Error ? error.message : "unknown error"}
      </p>
    );
  }

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold">Impact of changing {data.targetName}</h1>
          <p className="text-sm text-muted-foreground">
            {data.summary.totalAffected} affected components ·{" "}
            <Link href={`/graph/${encodeURIComponent(data.targetId)}`} className="text-accent hover:underline">
              View in Dependency Graph
            </Link>
          </p>
        </div>
        <ExportMermaidButton scope={data.targetId} depth={2} />
      </div>
      <ImpactSummaryList affected={data.affected} />
      <ImpactGraphHighlight targetId={data.targetId} />
    </div>
  );
}
