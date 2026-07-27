import Link from "next/link";
import { couplingBandStyle } from "@/lib/constants/coupling-scale";
import type { CouplingMetric } from "@/types/metrics";

export function CouplingDetailPanel({ metric, onClose }: { metric: CouplingMetric; onClose: () => void }) {
  const style = couplingBandStyle(metric.band);

  return (
    <div className="w-72 shrink-0 border-l border-surface-border p-4">
      <div className="mb-3 flex items-center justify-between">
        <h2 className="text-sm font-semibold">Coupling details</h2>
        <button type="button" onClick={onClose} className="text-xs text-muted-foreground hover:text-foreground">
          Close
        </button>
      </div>
      <div className="space-y-3">
        <p className="break-words text-sm font-medium">{metric.projectName}</p>
        <span className={`inline-flex w-fit items-center rounded-full border px-2 py-0.5 text-xs font-medium ${style.border} ${style.text} ${style.bg}`}>
          {style.label}
        </span>
        <dl className="grid grid-cols-2 gap-x-3 gap-y-2 text-sm">
          <div>
            <dt className="text-xs text-muted-foreground">Afferent coupling</dt>
            <dd className="font-medium">{metric.afferentCoupling}</dd>
          </div>
          <div>
            <dt className="text-xs text-muted-foreground">Efferent coupling</dt>
            <dd className="font-medium">{metric.efferentCoupling}</dd>
          </div>
          <div className="col-span-2">
            <dt className="text-xs text-muted-foreground">Instability</dt>
            <dd className="font-medium">{metric.instability.toFixed(2)}</dd>
          </div>
        </dl>
        <Link
          href={`/graph/${encodeURIComponent(metric.projectId)}`}
          className="block pt-2 text-sm text-accent hover:underline"
        >
          View in Dependency Graph
        </Link>
      </div>
    </div>
  );
}
