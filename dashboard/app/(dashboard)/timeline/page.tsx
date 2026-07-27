"use client";

import { TimelineFeed } from "@/components/domain/TimelineFeed";
import { TimelineTrendChart } from "@/components/domain/TimelineTrendChart";
import { useTimeline } from "@/hooks/useTimeline";

export default function TimelinePage() {
  const { entries, isLoading, isError, error } = useTimeline();

  if (isLoading) {
    return <p className="text-sm text-muted-foreground">Loading architecture metrics…</p>;
  }

  if (isError) {
    return (
      <p className="text-sm text-red-500">
        Failed to load metrics: {error instanceof Error ? error.message : "unknown error"}
      </p>
    );
  }

  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <div>
        <h1 className="text-xl font-semibold">Architecture Timeline</h1>
        <p className="text-sm text-muted-foreground">
          Polls the current architecture metrics every 15s and records a new entry whenever a
          totals change is detected — there&apos;s no server-side scan history yet, so this is a
          live, session-local feed rather than a browsable past.
        </p>
      </div>
      <TimelineTrendChart entries={entries} />
      <TimelineFeed entries={entries} />
    </div>
  );
}
