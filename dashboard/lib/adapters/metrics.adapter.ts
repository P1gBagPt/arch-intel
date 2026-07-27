import type { CouplingMetric, MetricsResponse } from "@/types/metrics";

export function adaptMetricsResponse(raw: MetricsResponse): MetricsResponse {
  return raw;
}

export function adaptCouplingMetrics(raw: CouplingMetric[]): CouplingMetric[] {
  return raw ?? [];
}
