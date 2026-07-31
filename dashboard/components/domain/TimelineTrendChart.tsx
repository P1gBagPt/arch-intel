"use client";

import { CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import type { TimelineEntry } from "@/hooks/useTimeline";

// Standardized on recharts for both this chart and the Coupling Heatmap treemap
// (06-dashboard.md §11 leaves the choice open for "the start of Phase 3" — picked here since
// both views now need one). Reuses existing design tokens rather than inventing new chart
// colors: classes/projects/interfaces/services map onto accent/stable/moderate/high.
const SERIES = [
  { key: "totalClasses", label: "Classes", color: "var(--accent)" },
  { key: "totalProjects", label: "Projects", color: "var(--coupling-stable)" },
  { key: "totalInterfaces", label: "Interfaces", color: "var(--coupling-moderate)" },
  { key: "totalServices", label: "Services", color: "var(--coupling-high)" },
] as const;

export function TimelineTrendChart({ entries }: { entries: TimelineEntry[] }) {
  if (entries.length < 2) {
    return (
      <p className="p-4 text-center text-xs text-muted-foreground">
        Trend chart appears once a change is detected across at least two readings.
      </p>
    );
  }

  const chronological = [...entries].reverse();
  const data = chronological.map((e) => ({
    time: new Date(e.timestamp).toLocaleTimeString(),
    totalClasses: e.metrics.totalClasses,
    totalProjects: e.metrics.totalProjects,
    totalInterfaces: e.metrics.totalInterfaces,
    totalServices: e.metrics.totalServices,
  }));

  return (
    <div className="rounded-md border border-surface-border p-2">
      <ResponsiveContainer width="100%" height={220}>
        <LineChart data={data} margin={{ top: 8, right: 12, bottom: 0, left: -12 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="var(--surface-border)" />
          <XAxis dataKey="time" tick={{ fontSize: 11, fill: "var(--muted-foreground)" }} />
          <YAxis tick={{ fontSize: 11, fill: "var(--muted-foreground)" }} allowDecimals={false} />
          <Tooltip
            contentStyle={{ background: "var(--background)", border: "1px solid var(--surface-border)", fontSize: 12 }}
          />
          <Legend wrapperStyle={{ fontSize: 12 }} />
          {SERIES.map((s) => (
            <Line
              key={s.key}
              type="monotone"
              dataKey={s.key}
              name={s.label}
              stroke={s.color}
              strokeWidth={2}
              dot={{ r: 3 }}
              isAnimationActive={false}
            />
          ))}
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}
