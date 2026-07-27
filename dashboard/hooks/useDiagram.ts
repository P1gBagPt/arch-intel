import { useMutation } from "@tanstack/react-query";
import { createApiClient } from "@/lib/api-client";
import { useRepo } from "@/hooks/useRepo";
import type { DiagramRequest, DiagramResponse } from "@/types/diagram";

// POST /diagram is an action, not a cached resource (06-dashboard.md §3.2) — a mutation,
// not a query. Backed by RequireRepoMaintainer + the "ai-operations" rate limiter server-side,
// so this should only ever fire on explicit user intent (an export button click), never on
// filter-change auto-refetch.
export function useDiagram() {
  const { repoId } = useRepo();

  return useMutation({
    mutationFn: async (request: DiagramRequest) => {
      const client = createApiClient(repoId);
      const envelope = await client.post<DiagramResponse>("/diagram", {
        scope: request.scope,
        depth: request.depth ?? 2,
        kinds: request.kinds,
        format: "mermaid",
      });
      return envelope.data;
    },
  });
}
