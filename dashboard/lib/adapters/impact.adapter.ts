import type { ImpactResponse } from "@/types/impact";

export function adaptImpactResponse(raw: ImpactResponse): ImpactResponse {
  return {
    targetId: raw.targetId,
    targetName: raw.targetName,
    affected: raw.affected ?? [],
    summary: raw.summary ?? { totalAffected: 0, byKind: {} },
  };
}
