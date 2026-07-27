"use client";

import type { HubConnection } from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef } from "react";
import { useRepo } from "@/hooks/useRepo";
import { queryKeys } from "@/lib/query-keys";
import { createArchitectureHubConnection } from "@/lib/signalr-client";
import { useLiveStatusStore } from "@/stores/live-status";
import type { JobCompletedEvent, JobFailedEvent } from "@/types/signalr-events";

// Mounted once in app/providers.tsx. Only subscribes to job:completed/job:failed — the only two
// hub events any backend code path actually raises (06-dashboard.md §6 describes a fuller
// graph-reconciliation flow via graph:updated, but that event has no producer, so there's
// nothing here to merge-patch into the graph query cache).
export function SignalRProvider({ children }: { children: React.ReactNode }) {
  const queryClient = useQueryClient();
  const { repoId } = useRepo();
  const connectionRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    // React 18 StrictMode double-invokes effects in dev — this one's cleanup calls
    // connection.stop() on the first (throwaway) instance, but that instance's own
    // start().then/.catch callbacks are still in flight and would otherwise clobber the
    // second (real) connection's state after cleanup already ran. `cancelled` guards every
    // async callback below so a stale instance can never write over the live one's state.
    let cancelled = false;
    const connection = createArchitectureHubConnection();
    connectionRef.current = connection;
    const { setConnectionState, pushEvent } = useLiveStatusStore.getState();

    connection.onreconnecting(() => {
      if (!cancelled) setConnectionState("reconnecting");
    });
    connection.onreconnected(() => {
      if (cancelled) return;
      setConnectionState("connected");
      // A real gap may have missed events — one full invalidation pass is simpler and safer
      // than trying to replay whatever was missed (06-dashboard.md §6.3).
      queryClient.invalidateQueries();
    });
    connection.onclose(() => {
      if (!cancelled) setConnectionState("disconnected");
    });

    connection.on("job:completed", (event: JobCompletedEvent) => {
      if (cancelled) return;
      pushEvent({
        id: `${event.jobId}-completed`,
        timestamp: new Date().toISOString(),
        kind: "job:completed",
        jobId: event.jobId,
        message: `Job ${event.jobId.slice(0, 8)} completed`,
      });
      queryClient.invalidateQueries({ queryKey: queryKeys.jobs.detail(repoId, event.jobId) });
    });

    connection.on("job:failed", (event: JobFailedEvent) => {
      if (cancelled) return;
      pushEvent({
        id: `${event.jobId}-failed`,
        timestamp: new Date().toISOString(),
        kind: "job:failed",
        jobId: event.jobId,
        message: `Job ${event.jobId.slice(0, 8)} failed: ${event.problem.title}`,
      });
      queryClient.invalidateQueries({ queryKey: queryKeys.jobs.detail(repoId, event.jobId) });
    });

    connection
      .start()
      .then(() => {
        if (!cancelled) setConnectionState("connected");
      })
      .catch(() => {
        if (!cancelled) setConnectionState("disconnected");
      });

    return () => {
      cancelled = true;
      connection.stop();
      connectionRef.current = null;
    };
  }, [queryClient, repoId]);

  return <>{children}</>;
}
