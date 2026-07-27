import type { PlannerJobKind, JobStatus } from "@/types/planning";

export interface PlannerHistoryEntry {
  id: string;
  kind: PlannerJobKind;
  jobId: string;
  text: string;
  status: JobStatus;
  submittedAt: string;
}

const KIND_LABEL: Record<PlannerJobKind, string> = {
  "implementation-plan": "Plan",
  "architecture-analysis": "Analysis",
};

// Session-scoped only (06-dashboard.md §4.7) — held in page state, not persisted or query-cached.
export function PlannerHistoryList({
  entries,
  activeId,
  onSelect,
}: {
  entries: PlannerHistoryEntry[];
  activeId: string | null;
  onSelect: (entry: PlannerHistoryEntry) => void;
}) {
  if (entries.length === 0) {
    return <p className="text-xs text-muted-foreground">No prompts submitted yet this session.</p>;
  }

  return (
    <ul className="space-y-1">
      {entries.map((entry) => (
        <li key={entry.id}>
          <button
            type="button"
            onClick={() => onSelect(entry)}
            className={`w-full rounded-md px-2 py-1.5 text-left text-xs transition-colors ${
              entry.id === activeId
                ? "bg-accent/10 text-accent"
                : "text-foreground/80 hover:bg-surface-border/50"
            }`}
          >
            <span className="mr-1.5 rounded bg-surface-border px-1 py-0.5 text-[10px] font-medium">
              {KIND_LABEL[entry.kind]}
            </span>
            <span className="truncate">{entry.text}</span>
          </button>
        </li>
      ))}
    </ul>
  );
}
