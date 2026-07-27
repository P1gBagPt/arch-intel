"use client";

import Link from "next/link";
import { useState } from "react";
import type { CircularDependency } from "@/types/metrics";

interface CircularDependencyBannerProps {
  cycles: CircularDependency[];
  projectNameById: Record<string, string>;
}

export function CircularDependencyBanner({ cycles, projectNameById }: CircularDependencyBannerProps) {
  const [dismissed, setDismissed] = useState(false);

  if (dismissed || cycles.length === 0) return null;

  return (
    <div className="rounded-md border border-coupling-high/30 bg-coupling-high/10 p-4 text-sm">
      <div className="flex items-start justify-between gap-4">
        <p className="font-medium text-coupling-high">
          {cycles.length} circular {cycles.length === 1 ? "dependency" : "dependencies"} detected
        </p>
        <button
          type="button"
          onClick={() => setDismissed(true)}
          className="shrink-0 text-xs text-muted-foreground hover:text-foreground"
        >
          Dismiss
        </button>
      </div>
      <ul className="mt-2 space-y-1">
        {cycles.map((cycle, i) => (
          <li key={i} className="flex flex-wrap items-center gap-1">
            {cycle.cycle.map((projectId, j) => (
              <span key={j} className="flex items-center gap-1">
                <Link href={`/graph/${encodeURIComponent(projectId)}`} className="hover:underline">
                  {projectNameById[projectId] ?? projectId}
                </Link>
                {j < cycle.cycle.length - 1 && <span className="text-muted-foreground">→</span>}
              </span>
            ))}
          </li>
        ))}
      </ul>
    </div>
  );
}
