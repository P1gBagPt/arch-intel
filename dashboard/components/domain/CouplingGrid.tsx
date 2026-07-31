"use client";

import { useMemo, useState } from "react";
import { ResponsiveContainer, Tooltip, Treemap } from "recharts";
import { couplingBandColorVar, couplingBandStyle } from "@/lib/constants/coupling-scale";
import type { CouplingMetric } from "@/types/metrics";

interface CouplingGridProps {
  metrics: CouplingMetric[];
  onSelect: (metric: CouplingMetric) => void;
  selectedProjectId?: string;
}

type ViewMode = "treemap" | "grid" | "table";

// CouplingMetricDto has no class-count-per-project field to size a treemap by (06-dashboard.md
// §4.6 assumes one) — using total coupling volume (afferent + efferent) instead. It's a real,
// meaningful metric already returned by the backend, not a fabricated size, and it reads
// naturally as "how structurally significant is this project" rather than "how big is it."
function treemapSize(metric: CouplingMetric): number {
  return Math.max(metric.afferentCoupling + metric.efferentCoupling, 1);
}

interface TreemapCellProps {
  x?: number;
  y?: number;
  width?: number;
  height?: number;
  projectId?: string;
  name?: string;
}

function TreemapCell(
  props: TreemapCellProps & { metricsById: Map<string, CouplingMetric>; selectedProjectId?: string; onSelect: (metric: CouplingMetric) => void },
) {
  const { x = 0, y = 0, width = 0, height = 0, projectId, metricsById, selectedProjectId, onSelect } = props;
  const metric = projectId ? metricsById.get(projectId) : undefined;
  if (!metric) return null;

  const style = couplingBandStyle(metric.band);
  const selected = metric.projectId === selectedProjectId;
  const showLabel = width > 56 && height > 28;

  return (
    <g
      onClick={() => onSelect(metric)}
      role="button"
      tabIndex={0}
      aria-label={`${metric.projectName}: ${style.label}, instability ${metric.instability.toFixed(2)}`}
      className="cursor-pointer"
    >
      <title>{`${metric.projectName} — ${style.label} (instability ${metric.instability.toFixed(2)})`}</title>
      <rect
        x={x}
        y={y}
        width={width}
        height={height}
        style={{
          fill: couplingBandColorVar(metric.band),
          fillOpacity: selected ? 0.55 : 0.3,
          stroke: "var(--background)",
          strokeWidth: 2,
        }}
      />
      {showLabel && (
        <text x={x + 6} y={y + 16} fontSize={11} fill="var(--foreground)" className="pointer-events-none">
          {metric.projectName}
        </text>
      )}
      {showLabel && (
        <text x={x + 6} y={y + 30} fontSize={10} fill="var(--muted-foreground)" className={`pointer-events-none ${style.text}`}>
          {style.label}
        </text>
      )}
    </g>
  );
}

// A treemap is preferred over a plain grid per 06-dashboard.md §4.6 ("lets project size carry
// information alongside coupling color") — kept as the default view, with grid/table as
// accessibility- and preference-driven fallbacks for users who'd rather scan a ranked list.
export function CouplingGrid({ metrics, onSelect, selectedProjectId }: CouplingGridProps) {
  const [view, setView] = useState<ViewMode>("treemap");

  const sorted = useMemo(() => [...metrics].sort((a, b) => b.instability - a.instability), [metrics]);
  const metricsById = useMemo(() => new Map(metrics.map((m) => [m.projectId, m])), [metrics]);
  const treemapData = useMemo(
    () => sorted.map((m) => ({ name: m.projectName, projectId: m.projectId, size: treemapSize(m) })),
    [sorted],
  );

  return (
    <div className="space-y-3">
      <div className="flex justify-end gap-2 text-xs">
        {(
          [
            { mode: "treemap", label: "Treemap" },
            { mode: "grid", label: "Grid" },
            { mode: "table", label: "Table" },
          ] as const
        ).map(({ mode, label }) => (
          <button
            key={mode}
            type="button"
            onClick={() => setView(mode)}
            className={`rounded-md border border-surface-border px-2 py-1 ${view === mode ? "bg-surface font-medium" : "text-muted-foreground"}`}
          >
            {label}
          </button>
        ))}
      </div>

      {view === "treemap" && (
        <div className="rounded-md border border-surface-border p-2">
          <ResponsiveContainer width="100%" height={360}>
            <Treemap
              data={treemapData}
              dataKey="size"
              aspectRatio={4 / 3}
              stroke="var(--background)"
              isAnimationActive={false}
              content={
                <TreemapCell metricsById={metricsById} selectedProjectId={selectedProjectId} onSelect={onSelect} />
              }
            >
              <Tooltip
                content={({ payload }) => {
                  const projectId = payload?.[0]?.payload?.projectId as string | undefined;
                  const metric = projectId ? metricsById.get(projectId) : undefined;
                  if (!metric) return null;
                  const style = couplingBandStyle(metric.band);
                  return (
                    <div className="rounded-md border border-surface-border bg-background p-2 text-xs shadow-lg">
                      <p className="font-medium">{metric.projectName}</p>
                      <p className={style.text}>{style.label}</p>
                      <p className="text-muted-foreground">Instability {metric.instability.toFixed(2)}</p>
                    </div>
                  );
                }}
              />
            </Treemap>
          </ResponsiveContainer>
        </div>
      )}

      {view === "grid" && (
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
      )}

      {view === "table" && (
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
