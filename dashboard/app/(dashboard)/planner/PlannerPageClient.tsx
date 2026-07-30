"use client";

import { useSearchParams } from "next/navigation";
import { useState } from "react";
import { PlannerHistoryList, type PlannerHistoryEntry } from "@/components/domain/PlannerHistoryList";
import { PlannerErrorPanel, PlannerResultPanel } from "@/components/domain/PlannerResultPanel";
import { PlannerLoadingState } from "@/components/domain/PlannerLoadingState";
import { PlannerPromptInput, type PlannerSubmission } from "@/components/domain/PlannerPromptInput";
import { useArchitectureAnalysis } from "@/hooks/useArchitectureAnalysis";
import { useImplementationPlan } from "@/hooks/useImplementationPlan";
import { useJobStatus } from "@/hooks/useJobStatus";
import type { PlannerJobKind } from "@/types/planning";

export function PlannerPageClient() {
  const searchParams = useSearchParams();
  const initialKind = searchParams.get("kind") === "architecture-analysis" ? "architecture-analysis" : undefined;
  const initialText = searchParams.get("prompt") ?? undefined;
  const initialScope = searchParams.get("scope") ?? undefined;

  const [history, setHistory] = useState<PlannerHistoryEntry[]>([]);
  const [activeId, setActiveId] = useState<string | null>(null);

  const implementationPlan = useImplementationPlan();
  const architectureAnalysis = useArchitectureAnalysis();

  const active = history.find((e) => e.id === activeId) ?? null;
  // Live status/result for the active entry always comes straight from useJobStatus (polling +
  // SignalR-invalidated) — history entries only keep the status they were accepted with, since
  // nothing here renders a per-entry live status badge.
  const jobStatus = useJobStatus(active?.jobId);

  function handleSubmit(submission: PlannerSubmission) {
    const id = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;

    const mutation =
      submission.kind === "implementation-plan"
        ? implementationPlan.mutateAsync({
            prompt: submission.text,
            scopeProjectIds: submission.scopeIds.length ? submission.scopeIds : undefined,
          })
        : architectureAnalysis.mutateAsync({
            question: submission.text,
            scopeNodeIds: submission.scopeIds.length ? submission.scopeIds : undefined,
          });

    mutation
      .then((accepted) => {
        setHistory((prev) => [
          {
            id,
            kind: submission.kind,
            jobId: accepted.jobId,
            text: submission.text,
            status: accepted.status,
            submittedAt: new Date().toISOString(),
          },
          ...prev,
        ]);
        setActiveId(id);
      })
      // Already surfaced via implementationPlan.error / architectureAnalysis.error below —
      // this catch only exists so the promise itself doesn't reject unhandled.
      .catch(() => {});
  }

  const isSubmitting = implementationPlan.isPending || architectureAnalysis.isPending;

  return (
    <div className="mx-auto flex max-w-5xl gap-6">
      <div className="flex-1 space-y-4">
        <h1 className="text-xl font-semibold">AI Planner</h1>

        <PlannerPromptInput
          onSubmit={handleSubmit}
          disabled={isSubmitting}
          initialKind={initialKind as PlannerJobKind | undefined}
          initialText={initialText}
          initialScope={initialScope}
        />

        {(implementationPlan.isError || architectureAnalysis.isError) && (
          <p className="text-sm text-red-500">
            Failed to submit:{" "}
            {(implementationPlan.error ?? architectureAnalysis.error) instanceof Error
              ? (implementationPlan.error ?? architectureAnalysis.error)?.message
              : "unknown error"}
          </p>
        )}

        {active && (
          <div className="space-y-3">
            <p className="text-xs text-muted-foreground">Job {active.jobId}</p>

            {jobStatus.isLoading && <PlannerLoadingState status="Pending" />}

            {jobStatus.data && jobStatus.data.status !== "Completed" && jobStatus.data.status !== "Failed" && (
              <PlannerLoadingState status={jobStatus.data.status} />
            )}

            {jobStatus.data?.status === "Failed" && jobStatus.data.problem && (
              <PlannerErrorPanel problem={jobStatus.data.problem} />
            )}

            {jobStatus.data?.status === "Completed" && jobStatus.data.result && (
              <PlannerResultPanel kind={active.kind} result={jobStatus.data.result} />
            )}
          </div>
        )}
      </div>

      <div className="w-56 shrink-0 space-y-2" data-testid="planner-history">
        <h2 className="text-sm font-semibold">History</h2>
        <PlannerHistoryList entries={history} activeId={activeId} onSelect={(entry) => setActiveId(entry.id)} />
      </div>
    </div>
  );
}
