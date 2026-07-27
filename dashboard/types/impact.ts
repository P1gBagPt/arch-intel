// Mirrors AffectedNodeDto / ImpactSummaryDto / ImpactResponseDto
// (src/Api/ArchIntel.Api/Contracts/ImpactResponseDto.cs) — GET /impact
export type RiskLevel = "Low" | "Medium" | "High";

export interface AffectedNode {
  id: string;
  kind: string;
  name: string;
  relation: string;
  depth: number;
  riskLevel: string;
}

export interface ImpactSummary {
  totalAffected: number;
  byKind: Record<string, number>;
}

export interface ImpactResponse {
  targetId: string;
  targetName: string;
  affected: AffectedNode[];
  summary: ImpactSummary;
}
