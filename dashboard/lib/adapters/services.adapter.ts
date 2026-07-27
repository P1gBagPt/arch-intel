import type { ServiceDetail, ServiceSummary } from "@/types/service";

export function adaptServiceSummary(raw: ServiceSummary): ServiceSummary {
  return {
    id: raw.id,
    name: raw.name,
    kind: raw.kind,
    projectId: raw.projectId,
    isHostedService: raw.isHostedService ?? false,
  };
}

export function adaptServiceDetail(raw: ServiceDetail): ServiceDetail {
  return {
    id: raw.id,
    name: raw.name,
    kind: raw.kind,
    projectId: raw.projectId,
    dependencies: raw.dependencies ?? [],
    callers: raw.callers ?? [],
    implements: raw.implements ?? [],
    tests: raw.tests ?? [],
  };
}
