"use client";

import { useState } from "react";
import type { PlannerJobKind } from "@/types/planning";

export interface PlannerSubmission {
  kind: PlannerJobKind;
  text: string;
  scopeIds: string[];
}

const MODES: { kind: PlannerJobKind; label: string; placeholder: string; scopeLabel: string; scopeHint: string }[] = [
  {
    kind: "implementation-plan",
    label: "Implementation Plan",
    placeholder: "Describe the change you want to implement…",
    scopeLabel: "Scope project IDs (optional)",
    scopeHint: "Comma-separated project IDs to scan. Leave blank to scan the whole repo.",
  },
  {
    kind: "architecture-analysis",
    label: "Architecture Analysis",
    placeholder: "Ask a question about the architecture…",
    scopeLabel: "Scope node ID",
    scopeHint: "A single node ID to analyze (only the first is used by the backend today).",
  },
];

export function PlannerPromptInput({
  onSubmit,
  disabled,
  initialKind,
  initialText,
  initialScope,
}: {
  onSubmit: (submission: PlannerSubmission) => void;
  disabled: boolean;
  initialKind?: PlannerJobKind;
  initialText?: string;
  initialScope?: string;
}) {
  const [kind, setKind] = useState<PlannerJobKind>(initialKind ?? "implementation-plan");
  const [text, setText] = useState(initialText ?? "");
  const [scope, setScope] = useState(initialScope ?? "");

  const mode = MODES.find((m) => m.kind === kind)!;

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!text.trim()) return;
    const scopeIds = scope
      .split(",")
      .map((s) => s.trim())
      .filter(Boolean);
    onSubmit({ kind, text: text.trim(), scopeIds });
    setText("");
    setScope("");
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3 rounded-lg border border-surface-border p-4">
      <div className="flex gap-2">
        {MODES.map((m) => (
          <button
            key={m.kind}
            type="button"
            onClick={() => setKind(m.kind)}
            className={`rounded-md px-3 py-1.5 text-sm font-medium transition-colors ${
              kind === m.kind
                ? "bg-accent/10 text-accent"
                : "text-foreground/70 hover:bg-surface-border/50"
            }`}
          >
            {m.label}
          </button>
        ))}
      </div>

      <textarea
        value={text}
        onChange={(e) => setText(e.target.value)}
        placeholder={mode.placeholder}
        rows={4}
        className="w-full rounded-md border border-surface-border bg-transparent p-2 text-sm outline-none focus:border-accent"
      />

      <div>
        <label className="block text-xs text-muted-foreground">{mode.scopeLabel}</label>
        <input
          value={scope}
          onChange={(e) => setScope(e.target.value)}
          placeholder={mode.scopeHint}
          className="mt-1 w-full rounded-md border border-surface-border bg-transparent p-2 text-sm outline-none focus:border-accent"
        />
      </div>

      <button
        type="submit"
        disabled={disabled || !text.trim()}
        className="rounded-md bg-accent px-4 py-2 text-sm font-medium text-white hover:opacity-90 disabled:opacity-50"
      >
        {disabled ? "Working…" : "Submit"}
      </button>
    </form>
  );
}
