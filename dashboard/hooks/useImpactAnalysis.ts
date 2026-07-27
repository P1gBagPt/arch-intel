import { useQuery } from "@tanstack/react-query";
import { createApiClient } from "@/lib/api-client";
import { adaptImpactResponse } from "@/lib/adapters/impact.adapter";
import { queryKeys } from "@/lib/query-keys";
import { useRepo } from "@/hooks/useRepo";
import type { ImpactResponse } from "@/types/impact";

export function useImpactAnalysis(targetId: string | undefined, maxDepth?: number) {
  const { repoId } = useRepo();

  return useQuery({
    queryKey: queryKeys.impact(repoId, targetId ?? "", maxDepth),
    queryFn: async ({ signal }) => {
      const client = createApiClient(repoId);
      const envelope = await client.get<ImpactResponse>(
        "/impact",
        { nodeId: targetId, maxDepth },
        signal,
      );
      return adaptImpactResponse(envelope.data);
    },
    enabled: !!targetId,
  });
}
