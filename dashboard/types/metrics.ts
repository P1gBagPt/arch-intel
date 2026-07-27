// Mirrors MetricsResponseDto / CouplingMetricDto / CircularDependencyDto
// (src/Api/ArchIntel.Api/Contracts/MetricsResponseDto.cs, CouplingDtos.cs) — GET /metrics, /metrics/coupling, /metrics/circular-dependencies
export interface MetricsResponse {
  totalProjects: number;
  totalClasses: number;
  totalInterfaces: number;
  totalServices: number;
  generatedAtUtc: string;
}

export type CouplingBand = "Green" | "Yellow" | "Red";

export interface CouplingMetric {
  projectId: string;
  projectName: string;
  afferentCoupling: number;
  efferentCoupling: number;
  instability: number;
  band: string;
}

export interface CircularDependency {
  cycle: string[];
  length: number;
}
