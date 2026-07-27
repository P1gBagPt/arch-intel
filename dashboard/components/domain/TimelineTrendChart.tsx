import type { TimelineEntry } from "@/hooks/useTimeline";

const WIDTH = 480;
const HEIGHT = 80;
const PADDING = 8;

// Deliberately not a charting library dependency (06-dashboard.md §11 leaves that choice open
// for Phase 3, to be picked once Timeline/Coupling actually need it) — a session only ever
// accumulates a handful of points, so a hand-rolled sparkline is proportionate.
export function TimelineTrendChart({ entries }: { entries: TimelineEntry[] }) {
  if (entries.length < 2) {
    return (
      <p className="p-4 text-center text-xs text-muted-foreground">
        Trend chart appears once a change is detected across at least two readings.
      </p>
    );
  }

  const chronological = [...entries].reverse();
  const values = chronological.map((e) => e.metrics.totalClasses);
  const min = Math.min(...values);
  const max = Math.max(...values);
  const range = max - min || 1;

  const points = values
    .map((v, i) => {
      const x = PADDING + (i / (values.length - 1)) * (WIDTH - PADDING * 2);
      const y = HEIGHT - PADDING - ((v - min) / range) * (HEIGHT - PADDING * 2);
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    })
    .join(" ");

  return (
    <div className="rounded-md border border-surface-border p-2">
      <svg viewBox={`0 0 ${WIDTH} ${HEIGHT}`} className="w-full" role="img" aria-label="Class count trend this session">
        <polyline points={points} fill="none" stroke="var(--accent)" strokeWidth={2} />
      </svg>
      <p className="mt-1 text-center text-xs text-muted-foreground">
        Class count this session: {min.toLocaleString()} → {max.toLocaleString()}
      </p>
    </div>
  );
}
