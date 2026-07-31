import type { TimelineEntry } from "@/hooks/useTimeline";

function formatDelta(delta: TimelineEntry["delta"]) {
  if (!delta) return null;
  const parts: string[] = [];
  const push = (n: number, singular: string, plural: string) => {
    if (n === 0) return;
    const label = Math.abs(n) === 1 ? singular : plural;
    parts.push(`${n > 0 ? "+" : ""}${n} ${label}`);
  };
  push(delta.classes, "class", "classes");
  push(delta.projects, "project", "projects");
  push(delta.interfaces, "interface", "interfaces");
  push(delta.services, "service", "services");
  return parts.join(", ");
}

export function TimelineFeed({
  entries,
  selectedId,
  onSelect,
}: {
  entries: TimelineEntry[];
  selectedId?: string | null;
  onSelect?: (entry: TimelineEntry) => void;
}) {
  if (entries.length === 0) {
    return <p className="py-8 text-center text-sm text-muted-foreground">Waiting for the first reading…</p>;
  }

  return (
    <ul className="space-y-2">
      {entries.map((entry, i) => {
        const isBaseline = entry.delta === null;
        const deltaText = formatDelta(entry.delta);
        const clickable = !isBaseline && !!onSelect;
        return (
          <li
            key={entry.id}
            onClick={clickable ? () => onSelect?.(entry) : undefined}
            className={`rounded-md border p-3 text-sm ${
              entry.id === selectedId ? "border-accent" : "border-surface-border"
            } ${clickable ? "cursor-pointer hover:bg-surface" : ""}`}
          >
            <div className="flex items-center justify-between">
              <span className="font-medium">{i === 0 ? "Now" : new Date(entry.timestamp).toLocaleTimeString()}</span>
              {!isBaseline && <span className="text-xs text-accent">Change detected</span>}
            </div>
            <p className="mt-1 text-muted-foreground">
              {entry.metrics.totalClasses.toLocaleString()} classes, {entry.metrics.totalProjects} projects,{" "}
              {entry.metrics.totalInterfaces} interfaces, {entry.metrics.totalServices} services
            </p>
            {deltaText && <p className="mt-1 font-medium">Changes: {deltaText}</p>}
            {isBaseline && <p className="mt-1 text-xs text-muted-foreground">Baseline reading</p>}
            {clickable && <p className="mt-1 text-xs text-accent">What changed →</p>}
          </li>
        );
      })}
    </ul>
  );
}
