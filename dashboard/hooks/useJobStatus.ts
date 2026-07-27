import { useQuery } from "@tanstack/react-query";
import { createApiClient } from "@/lib/api-client";
import { queryKeys } from "@/lib/query-keys";
import { useRepo } from "@/hooks/useRepo";
import type { JobStatusResponseDto } from "@/types/planning";

const POLL_INTERVAL_MS = 3_000;

function isTerminal(status: string | undefined): boolean {
  return status === "Completed" || status === "Failed";
}

// SignalRProvider already invalidates queryKeys.jobs.detail on job:completed/job:failed, which
// would refetch this instantly — but the hub can be down (see useLiveStatusStore's
// connectionState) with no other signal that the job finished, so this also self-polls as a
// fallback and simply stops once the SignalR-driven refetch (or its own poll) lands a terminal
// status.
export function useJobStatus(jobId: string | undefined) {
  const { repoId } = useRepo();

  return useQuery({
    queryKey: queryKeys.jobs.detail(repoId, jobId ?? ""),
    queryFn: async ({ signal }) => {
      const client = createApiClient(repoId);
      const envelope = await client.get<JobStatusResponseDto>(`/jobs/${jobId}`, undefined, signal);
      return envelope.data;
    },
    enabled: !!jobId,
    refetchInterval: (query) => (isTerminal(query.state.data?.status) ? false : POLL_INTERVAL_MS),
  });
}
