import { useMutation } from "@tanstack/react-query";
import { createApiClient } from "@/lib/api-client";
import { useRepo } from "@/hooks/useRepo";
import type { ArchitectureAnalysisRequest, JobAcceptedDto } from "@/types/planning";

// POST /architecture-analysis is also 202 Accepted + a jobId (see useImplementationPlan) — pair
// with useJobStatus(jobId) for the eventual ArchitectureAnalysisResult.
export function useArchitectureAnalysis() {
  const { repoId } = useRepo();

  return useMutation({
    mutationFn: async (request: ArchitectureAnalysisRequest) => {
      const client = createApiClient(repoId);
      const envelope = await client.post<JobAcceptedDto>("/architecture-analysis", request);
      return envelope.data;
    },
  });
}
