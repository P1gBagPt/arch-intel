import type {
  ArchitectureAnalysisResult,
  ImplementationPlanResult,
  JobProblemSummary,
  PlannerJobKind,
} from "@/types/planning";

const RISK_STYLES: Record<string, string> = {
  low: "border-coupling-stable text-coupling-stable bg-coupling-stable/10",
  medium: "border-coupling-moderate text-coupling-moderate bg-coupling-moderate/10",
  high: "border-coupling-high text-coupling-high bg-coupling-high/10",
};

function riskStyle(riskLevel: string): string {
  return RISK_STYLES[riskLevel.toLowerCase()] ?? "border-surface-border text-muted-foreground bg-surface-border/20";
}

function StringList({ label, items }: { label: string; items: string[] }) {
  return (
    <div>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      {items.length === 0 ? (
        <dd className="mt-1 text-sm text-muted-foreground">None</dd>
      ) : (
        <dd className="mt-1 space-y-0.5 text-sm">
          {items.map((item) => (
            <div key={item} className="rounded bg-surface-border/30 px-2 py-1 font-mono text-xs">
              {item}
            </div>
          ))}
        </dd>
      )}
    </div>
  );
}

function ImplementationPlanView({ result }: { result: ImplementationPlanResult }) {
  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <span className={`inline-flex items-center rounded-full border px-2 py-0.5 text-xs font-medium ${riskStyle(result.riskLevel)}`}>
          Risk: {result.riskLevel}
        </span>
        <span className="text-xs text-muted-foreground">Effort: {result.estimatedEffort}</span>
      </div>
      {(result.riskLevel === "Unknown" || result.estimatedEffort.toLowerCase().includes("placeholder")) && (
        <p className="rounded-md border border-coupling-moderate/40 bg-coupling-moderate/5 px-3 py-2 text-xs text-muted-foreground">
          The planning service is currently a placeholder with no LLM wired up — risk/effort aren&apos;t
          real estimates yet, and the prompt text itself doesn&apos;t influence this result. Affected
          projects/services below come from a real dependency-graph scan of the requested scope.
        </p>
      )}
      <dl className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <StringList label="Affected projects" items={result.affectedProjects} />
        <StringList label="Modified services" items={result.modifiedServices} />
        <StringList label="New files" items={result.newFiles} />
        <StringList label="Database changes" items={result.databaseChanges} />
        <StringList label="Tests required" items={result.testsRequired} />
      </dl>
    </div>
  );
}

function ArchitectureAnalysisView({ result }: { result: ArchitectureAnalysisResult }) {
  return (
    <div className="space-y-4">
      <p className="text-sm">{result.summary}</p>
      <dl className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <StringList label="Affected node IDs" items={result.affectedNodeIds} />
        <StringList label="Recommendations" items={result.recommendations} />
      </dl>
    </div>
  );
}

export function PlannerResultPanel({
  kind,
  result,
}: {
  kind: PlannerJobKind;
  result: ImplementationPlanResult | ArchitectureAnalysisResult;
}) {
  return (
    <div className="rounded-lg border border-surface-border p-4">
      {kind === "implementation-plan" ? (
        <ImplementationPlanView result={result as ImplementationPlanResult} />
      ) : (
        <ArchitectureAnalysisView result={result as ArchitectureAnalysisResult} />
      )}
    </div>
  );
}

export function PlannerErrorPanel({ problem }: { problem: JobProblemSummary }) {
  return (
    <div className="rounded-lg border border-coupling-high/40 bg-coupling-high/5 p-4 text-sm text-coupling-high">
      <p className="font-medium">Job failed ({problem.status})</p>
      <p className="mt-1">{problem.title}</p>
    </div>
  );
}
