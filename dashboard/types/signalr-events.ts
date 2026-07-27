// Mirrors src/Api/ArchIntel.Api/Realtime/RealtimeEvents.cs. Only JobCompleted/JobFailed are
// wired to an actual producer anywhere in the backend (confirmed by reading every call site of
// IArchitectureChangeNotifier) — ScanProgress/GraphUpdated/MetricsUpdated exist as event shapes
// with no code path that ever raises them, so this client only listens for the two that fire.
export interface JobProblemSummary {
  title: string;
  status: number;
}

export interface JobCompletedEvent {
  jobId: string;
  status: string;
}

export interface JobFailedEvent {
  jobId: string;
  status: string;
  problem: JobProblemSummary;
}
