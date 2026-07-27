import { useQuery } from "@tanstack/react-query";
import { adaptCouplingMetrics, adaptMetricsResponse } from "@/lib/adapters/metrics.adapter";
import { createApiClient } from "@/lib/api-client";
import { queryKeys } from "@/lib/query-keys";
import { useRepo } from "@/hooks/useRepo";
import type { CircularDependency, CouplingMetric, MetricsResponse } from "@/types/metrics";

export function useMetrics() {
  const { repoId } = useRepo();

  return useQuery({
    queryKey: queryKeys.metrics.all(repoId),
    queryFn: async ({ signal }) => {
      const client = createApiClient(repoId);
      const envelope = await client.get<MetricsResponse>("/metrics", undefined, signal);
      return adaptMetricsResponse(envelope.data);
    },
    staleTime: 60_000,
  });
}

export function useCouplingMetrics() {
  const { repoId } = useRepo();

  return useQuery({
    queryKey: queryKeys.metrics.coupling(repoId),
    queryFn: async ({ signal }) => {
      const client = createApiClient(repoId);
      const envelope = await client.get<CouplingMetric[]>("/metrics/coupling", undefined, signal);
      return adaptCouplingMetrics(envelope.data);
    },
    staleTime: 60_000,
  });
}

export function useCircularDependencies() {
  const { repoId } = useRepo();

  return useQuery({
    queryKey: queryKeys.metrics.circularDependencies(repoId),
    queryFn: async ({ signal }) => {
      const client = createApiClient(repoId);
      const envelope = await client.get<CircularDependency[]>("/metrics/circular-dependencies", undefined, signal);
      return envelope.data ?? [];
    },
    staleTime: 60_000,
  });
}
