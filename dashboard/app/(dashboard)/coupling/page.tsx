"use client";

import { useMemo, useState } from "react";
import { CircularDependencyBanner } from "@/components/domain/CircularDependencyBanner";
import { CouplingDetailPanel } from "@/components/domain/CouplingDetailPanel";
import { CouplingGrid } from "@/components/domain/CouplingGrid";
import { CouplingLegend } from "@/components/domain/CouplingLegend";
import { useCircularDependencies, useCouplingMetrics } from "@/hooks/useMetrics";
import { useProjects } from "@/hooks/useProjects";
import type { CouplingMetric } from "@/types/metrics";

export default function CouplingHeatmapPage() {
  const { data: metrics, isLoading, isError, error } = useCouplingMetrics();
  const { data: cycles } = useCircularDependencies();
  const { data: projects } = useProjects();
  const [selected, setSelected] = useState<CouplingMetric | null>(null);

  const projectNameById = useMemo(() => {
    return Object.fromEntries((projects ?? []).map((p) => [p.id, p.name]));
  }, [projects]);

  if (isLoading) {
    return <p className="text-sm text-muted-foreground">Loading coupling metrics…</p>;
  }

  if (isError || !metrics) {
    return (
      <p className="text-sm text-red-500">
        Failed to load coupling metrics: {error instanceof Error ? error.message : "unknown error"}
      </p>
    );
  }

  return (
    <div className="mx-auto max-w-4xl space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold">Coupling Heatmap</h1>
        <CouplingLegend />
      </div>

      {cycles && cycles.length > 0 && (
        <CircularDependencyBanner cycles={cycles} projectNameById={projectNameById} />
      )}

      <div className="flex gap-4">
        <div className="flex-1">
          <CouplingGrid metrics={metrics} onSelect={setSelected} selectedProjectId={selected?.projectId} />
        </div>
        {selected && <CouplingDetailPanel metric={selected} onClose={() => setSelected(null)} />}
      </div>
    </div>
  );
}
