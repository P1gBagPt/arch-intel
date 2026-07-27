import { useMutation } from "@tanstack/react-query";
import { createApiClient } from "@/lib/api-client";
import { useRepo } from "@/hooks/useRepo";
import type { ImplementationPlanRequest, JobAcceptedDto } from "@/types/planning";

// POST /implementation-plan is 202 Accepted + a jobId, not the plan itself (RequireRepoMaintainer
// + the "ai-operations" rate limiter server-side) — the caller pairs this with useJobStatus(jobId)
// to observe the eventual Completed/Failed result.
export function useImplementationPlan() {
  const { repoId } = useRepo();

  return useMutation({
    mutationFn: async (request: ImplementationPlanRequest) => {
      const client = createApiClient(repoId);
      const envelope = await client.post<JobAcceptedDto>("/implementation-plan", request);
      return envelope.data;
    },
  });
}
