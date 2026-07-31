"use client";

import { useState } from "react";
import { SnapshotDiffDrawer } from "@/components/domain/SnapshotDiffDrawer";
import { TimelineFeed } from "@/components/domain/TimelineFeed";
import { TimelineTrendChart } from "@/components/domain/TimelineTrendChart";
import { useTimeline } from "@/hooks/useTimeline";

export default function TimelinePage() {
  const { entries, isLoading, isError, error } = useTimeline();
  const [selectedId, setSelectedId] = useState<string | null>(null);

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

  const selected = entries.find((e) => e.id === selectedId) ?? null;

  return (
    <div className="mx-auto flex max-w-4xl gap-4">
      <div className="flex-1 space-y-4">
        <div>
          <h1 className="text-xl font-semibold">Architecture Timeline</h1>
          <p className="text-sm text-muted-foreground">
            Polls the current architecture metrics every 15s and records a new entry whenever a
            totals change is detected — there&apos;s no server-side scan history yet, so this is a
            live, session-local feed rather than a browsable past.
          </p>
        </div>
        <TimelineTrendChart entries={entries} />
        <TimelineFeed entries={entries} selectedId={selectedId} onSelect={(entry) => setSelectedId(entry.id)} />
      </div>

      {selected && <SnapshotDiffDrawer entry={selected} onClose={() => setSelectedId(null)} />}
    </div>
  );
}
