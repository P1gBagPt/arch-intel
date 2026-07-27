import type { JobProblemSummary } from "@/types/signalr-events";

export type { JobProblemSummary };

// Mirrors src/Api/ArchIntel.Api/Planning/PlanningContracts.cs
export interface ImplementationPlanRequest {
  prompt: string;
  scopeProjectIds?: string[];
}

export interface ImplementationPlanResult {
  affectedProjects: string[];
  newFiles: string[];
  modifiedServices: string[];
  databaseChanges: string[];
  testsRequired: string[];
  riskLevel: string;
  estimatedEffort: string;
}

export interface ArchitectureAnalysisRequest {
  question: string;
  scopeNodeIds?: string[];
}

export interface ArchitectureAnalysisResult {
  summary: string;
  affectedNodeIds: string[];
  recommendations: string[];
}

// Mirrors src/Api/ArchIntel.Api/Contracts/JobDtos.cs. `status` matches the SignalR event's
// `status` string exactly ("Pending" | "Running" | "Completed" | "Failed").
export type JobStatus = "Pending" | "Running" | "Completed" | "Failed";

export interface JobAcceptedDto {
  jobId: string;
  status: JobStatus;
}

// `result` has no discriminator on the wire — the caller must know which endpoint it POSTed to
// in order to narrow this to ImplementationPlanResult vs ArchitectureAnalysisResult.
export interface JobStatusResponseDto {
  jobId: string;
  status: JobStatus;
  progressPercent: number | null;
  result: ImplementationPlanResult | ArchitectureAnalysisResult | null;
  problem: JobProblemSummary | null;
}

export type PlannerJobKind = "implementation-plan" | "architecture-analysis";
