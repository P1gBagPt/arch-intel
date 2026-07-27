import { useQuery } from "@tanstack/react-query";
import { createApiClient } from "@/lib/api-client";
import { adaptServiceDetail, adaptServiceSummary } from "@/lib/adapters/services.adapter";
import { queryKeys } from "@/lib/query-keys";
import { useRepo } from "@/hooks/useRepo";
import type { ServiceDetail, ServiceSummary } from "@/types/service";

export function useServices() {
  const { repoId } = useRepo();

  return useQuery({
    queryKey: queryKeys.services.all(repoId),
    queryFn: async ({ signal }) => {
      const client = createApiClient(repoId);
      const envelope = await client.get<ServiceSummary[]>("/services", { limit: 500 }, signal);
      return envelope.data.map(adaptServiceSummary);
    },
    staleTime: 60_000,
  });
}

export function useServiceDetail(serviceId: string | undefined) {
  const { repoId } = useRepo();

  return useQuery({
    queryKey: queryKeys.services.detail(repoId, serviceId ?? ""),
    queryFn: async ({ signal }) => {
      const client = createApiClient(repoId);
      const envelope = await client.get<ServiceDetail>(`/services/${serviceId}`, undefined, signal);
      return adaptServiceDetail(envelope.data);
    },
    enabled: !!serviceId,
  });
}
