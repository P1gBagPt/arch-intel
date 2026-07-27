import { useQuery } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { adaptMetricsResponse } from "@/lib/adapters/metrics.adapter";
import { createApiClient } from "@/lib/api-client";
import { queryKeys } from "@/lib/query-keys";
import { useRepo } from "@/hooks/useRepo";
import type { MetricsResponse } from "@/types/metrics";

const POLL_INTERVAL_MS = 15_000;

export interface TimelineDelta {
  classes: number;
  projects: number;
  interfaces: number;
  services: number;
}

export interface TimelineEntry {
  id: string;
  timestamp: string;
  metrics: MetricsResponse;
  delta: TimelineDelta | null; // null marks the session's first (baseline) reading
}

// No server-side scan history exists (05-rest-api.md/06-dashboard.md §11 — /snapshots only ever
// returns the current live scan, and the metrics:updated SignalR event has no producer anywhere
// in the backend, confirmed by reading the actual call sites). This polls GET /metrics on an
// interval and diffs client-side instead of subscribing to a push that would never fire — a
// real re-scan on the backend while this page is open surfaces as a genuine delta entry on the
// next poll, same end result the plan's timeline describes, just pull- rather than push-driven.
export function useTimeline() {
  const { repoId } = useRepo();
  const [entries, setEntries] = useState<TimelineEntry[]>([]);
  const lastRef = useRef<MetricsResponse | null>(null);
  const counterRef = useRef(0);

  const query = useQuery({
    queryKey: [...queryKeys.metrics.all(repoId), "timeline-poll"],
    queryFn: async ({ signal }) => {
      const client = createApiClient(repoId);
      const envelope = await client.get<MetricsResponse>("/metrics", undefined, signal);
      return adaptMetricsResponse(envelope.data);
    },
    refetchInterval: POLL_INTERVAL_MS,
    refetchIntervalInBackground: true,
  });

  useEffect(() => {
    const metrics = query.data;
    if (!metrics) return;
    const prev = lastRef.current;

    if (!prev) {
      lastRef.current = metrics;
      setEntries([{ id: `${counterRef.current++}`, timestamp: metrics.generatedAtUtc, metrics, delta: null }]);
      return;
    }

    const delta: TimelineDelta = {
      classes: metrics.totalClasses - prev.totalClasses,
      projects: metrics.totalProjects - prev.totalProjects,
      interfaces: metrics.totalInterfaces - prev.totalInterfaces,
      services: metrics.totalServices - prev.totalServices,
    };
    const unchanged = delta.classes === 0 && delta.projects === 0 && delta.interfaces === 0 && delta.services === 0;
    if (unchanged) return;

    lastRef.current = metrics;
    setEntries((prevEntries) => [
      { id: `${counterRef.current++}`, timestamp: metrics.generatedAtUtc, metrics, delta },
      ...prevEntries,
    ]);
  }, [query.data]);

  return { entries, isLoading: query.isLoading, isError: query.isError, error: query.error };
}
