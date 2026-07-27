import type { JobStatus } from "@/types/planning";

// There's no streaming/progress from the backend today (JobStatusResponseDto.ProgressPercent is
// declared but never populated by RunJobAsync) — this is a plain status label, not a progress bar.
const STATUS_COPY: Record<JobStatus, string> = {
  Pending: "Queued…",
  Running: "Analyzing the graph…",
  Completed: "Done",
  Failed: "Failed",
};

export function PlannerLoadingState({ status }: { status: JobStatus }) {
  return (
    <div className="flex items-center gap-2 rounded-lg border border-surface-border p-4 text-sm text-muted-foreground">
      <span className="h-2 w-2 animate-pulse rounded-full bg-accent" />
      {STATUS_COPY[status]}
    </div>
  );
}
