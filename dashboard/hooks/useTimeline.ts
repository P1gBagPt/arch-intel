import { useQuery } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { adaptMetricsResponse } from "@/lib/adapters/metrics.adapter";
import { adaptProjectSummary } from "@/lib/adapters/projects.adapter";
import { createApiClient } from "@/lib/api-client";
import { queryKeys } from "@/lib/query-keys";
import { useRepo } from "@/hooks/useRepo";
import type { MetricsResponse } from "@/types/metrics";
import type { ProjectSummary } from "@/types/project";

const POLL_INTERVAL_MS = 15_000;

export interface TimelineDelta {
  classes: number;
  projects: number;
  interfaces: number;
  services: number;
}

// Real project-level diff, not a fabricated per-class symbol list — GET /projects is the closest
// thing to a per-entity resource cheap enough to poll every tick (GET /graph can be thousands of
// nodes with no projectId on GraphNode to group by). When a re-scan only changes class/interface/
// service counts *within* existing projects, added/removed stay empty and the drawer says so
// honestly rather than inventing symbol names the backend never returned.
export interface TimelineProjectDiff {
  added: ProjectSummary[];
  removed: ProjectSummary[];
}

export interface TimelineEntry {
  id: string;
  timestamp: string;
  metrics: MetricsResponse;
  delta: TimelineDelta | null; // null marks the session's first (baseline) reading
  projectDiff: TimelineProjectDiff | null; // null on the baseline entry (nothing to diff against)
}

interface TimelineSnapshot {
  metrics: MetricsResponse;
  projects: ProjectSummary[];
}

// No server-side scan history exists (05-rest-api.md/06-dashboard.md §11 — /snapshots only ever
// returns the current live scan, and the metrics:updated SignalR event has no producer anywhere
// in the backend, confirmed by reading the actual call sites). This polls GET /metrics + GET
// /projects together on an interval and diffs client-side instead of subscribing to a push that
// would never fire — a real re-scan on the backend while this page is open surfaces as a genuine
// delta entry on the next poll, same end result the plan's timeline describes, just pull- rather
// than push-driven. The two resources are fetched as one query (not two independent polls) so a
// metrics tick is never paired against a projects snapshot from a different tick.
export function useTimeline() {
  const { repoId } = useRepo();
  const [entries, setEntries] = useState<TimelineEntry[]>([]);
  const lastRef = useRef<TimelineSnapshot | null>(null);
  const counterRef = useRef(0);

  const query = useQuery({
    queryKey: [...queryKeys.metrics.all(repoId), "timeline-poll"],
    queryFn: async ({ signal }): Promise<TimelineSnapshot> => {
      const client = createApiClient(repoId);
      const [metricsEnvelope, projectsEnvelope] = await Promise.all([
        client.get<MetricsResponse>("/metrics", undefined, signal),
        client.get<ProjectSummary[]>("/projects", { limit: 500 }, signal),
      ]);
      return {
        metrics: adaptMetricsResponse(metricsEnvelope.data),
        projects: projectsEnvelope.data.map(adaptProjectSummary),
      };
    },
    refetchInterval: POLL_INTERVAL_MS,
    refetchIntervalInBackground: true,
  });

  useEffect(() => {
    const snapshot = query.data;
    if (!snapshot) return;
    const prev = lastRef.current;

    if (!prev) {
      lastRef.current = snapshot;
      setEntries([
        {
          id: `${counterRef.current++}`,
          timestamp: snapshot.metrics.generatedAtUtc,
          metrics: snapshot.metrics,
          delta: null,
          projectDiff: null,
        },
      ]);
      return;
    }

    const delta: TimelineDelta = {
      classes: snapshot.metrics.totalClasses - prev.metrics.totalClasses,
      projects: snapshot.metrics.totalProjects - prev.metrics.totalProjects,
      interfaces: snapshot.metrics.totalInterfaces - prev.metrics.totalInterfaces,
      services: snapshot.metrics.totalServices - prev.metrics.totalServices,
    };
    const unchanged = delta.classes === 0 && delta.projects === 0 && delta.interfaces === 0 && delta.services === 0;
    if (unchanged) return;

    const prevIds = new Set(prev.projects.map((p) => p.id));
    const currentIds = new Set(snapshot.projects.map((p) => p.id));
    const projectDiff: TimelineProjectDiff = {
      added: snapshot.projects.filter((p) => !prevIds.has(p.id)),
      removed: prev.projects.filter((p) => !currentIds.has(p.id)),
    };

    lastRef.current = snapshot;
    setEntries((prevEntries) => [
      {
        id: `${counterRef.current++}`,
        timestamp: snapshot.metrics.generatedAtUtc,
        metrics: snapshot.metrics,
        delta,
        projectDiff,
      },
      ...prevEntries,
    ]);
  }, [query.data]);

  return {
    entries,
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
  };
}
