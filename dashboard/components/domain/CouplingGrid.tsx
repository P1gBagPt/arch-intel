"use client";

import { useMemo, useState } from "react";
import { couplingBandStyle } from "@/lib/constants/coupling-scale";
import type { CouplingMetric } from "@/types/metrics";

interface CouplingGridProps {
  metrics: CouplingMetric[];
  onSelect: (metric: CouplingMetric) => void;
  selectedProjectId?: string;
}

type ViewMode = "grid" | "table";

// A treemap (06-dashboard.md §4.6) needs a size dimension — project class/node count — that
// CouplingMetricDto doesn't carry; a card grid with a table fallback covers the same accessibility
// goal (§4.6's "toggle... for users who prefer scanning a ranked list") without inventing a size
// metric the real API doesn't provide.
export function CouplingGrid({ metrics, onSelect, selectedProjectId }: CouplingGridProps) {
  const [view, setView] = useState<ViewMode>("grid");

  const sorted = useMemo(() => [...metrics].sort((a, b) => b.instability - a.instability), [metrics]);

  return (
    <div className="space-y-3">
      <div className="flex justify-end gap-2 text-xs">
        <button
          type="button"
          onClick={() => setView("grid")}
          className={`rounded-md border border-surface-border px-2 py-1 ${view === "grid" ? "bg-surface font-medium" : "text-muted-foreground"}`}
        >
          Grid
        </button>
        <button
          type="button"
          onClick={() => setView("table")}
          className={`rounded-md border border-surface-border px-2 py-1 ${view === "table" ? "bg-surface font-medium" : "text-muted-foreground"}`}
        >
          Table
        </button>
      </div>

      {view === "grid" ? (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          {sorted.map((metric) => {
            const style = couplingBandStyle(metric.band);
            const selected = metric.projectId === selectedProjectId;
            return (
              <button
                key={metric.projectId}
                type="button"
                onClick={() => onSelect(metric)}
                className={`rounded-md border-2 p-3 text-left transition-shadow ${style.border} ${style.bg} ${selected ? "ring-2 ring-accent" : ""}`}
              >
                <p className="truncate text-sm font-medium">{metric.projectName}</p>
                <p className={`text-xs ${style.text}`}>{style.label}</p>
                <p className="mt-1 text-xs text-muted-foreground">Instability {metric.instability.toFixed(2)}</p>
              </button>
            );
          })}
        </div>
      ) : (
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-surface-border text-left text-xs text-muted-foreground">
              <th className="pb-2 font-medium">Project</th>
              <th className="pb-2 font-medium">Afferent</th>
              <th className="pb-2 font-medium">Efferent</th>
              <th className="pb-2 font-medium">Instability</th>
              <th className="pb-2 font-medium">Band</th>
            </tr>
          </thead>
          <tbody>
            {sorted.map((metric) => {
              const style = couplingBandStyle(metric.band);
              return (
                <tr
                  key={metric.projectId}
                  onClick={() => onSelect(metric)}
                  className={`cursor-pointer border-b border-surface-border last:border-0 hover:bg-surface ${
                    metric.projectId === selectedProjectId ? "bg-surface" : ""
                  }`}
                >
                  <td className="py-2">{metric.projectName}</td>
                  <td className="py-2">{metric.afferentCoupling}</td>
                  <td className="py-2">{metric.efferentCoupling}</td>
                  <td className="py-2">{metric.instability.toFixed(2)}</td>
                  <td className={`py-2 ${style.text}`}>{style.label}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </div>
  );
}
