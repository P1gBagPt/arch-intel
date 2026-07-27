import { useQuery } from "@tanstack/react-query";
import { createApiClient } from "@/lib/api-client";
import { adaptGraphResponse } from "@/lib/adapters/graph.adapter";
import { queryKeys } from "@/lib/query-keys";
import { useRepo } from "@/hooks/useRepo";
import type { GraphFilters, GraphResponse } from "@/types/graph";

export function useDependencyGraph(filters: GraphFilters = {}) {
  const { repoId } = useRepo();

  return useQuery({
    queryKey: queryKeys.graph.filtered(repoId, filters),
    queryFn: async ({ signal }) => {
      const client = createApiClient(repoId);
      const envelope = await client.get<GraphResponse>(
        "/graph",
        {
          scope: filters.scope,
          depth: filters.depth,
          kinds: filters.kinds,
          full: filters.full,
        },
        signal,
      );
      return adaptGraphResponse(envelope.data);
    },
    staleTime: 30_000,
  });
}
