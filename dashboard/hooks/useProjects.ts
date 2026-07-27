import { useQuery } from "@tanstack/react-query";
import { createApiClient } from "@/lib/api-client";
import { adaptProjectSummary } from "@/lib/adapters/projects.adapter";
import { queryKeys } from "@/lib/query-keys";
import { useRepo } from "@/hooks/useRepo";
import type { ProjectSummary } from "@/types/project";

export function useProjects() {
  const { repoId } = useRepo();

  return useQuery({
    queryKey: queryKeys.projects.all(repoId),
    queryFn: async ({ signal }) => {
      const client = createApiClient(repoId);
      const envelope = await client.get<ProjectSummary[]>("/projects", { limit: 500 }, signal);
      return envelope.data.map(adaptProjectSummary);
    },
    staleTime: 5 * 60_000,
  });
}
