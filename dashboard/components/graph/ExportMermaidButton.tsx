"use client";

import { useState } from "react";
import { MermaidPreview } from "@/components/graph/MermaidPreview";
import { Modal } from "@/components/ui/Modal";
import { useDiagram } from "@/hooks/useDiagram";
import type { DiagramRequest } from "@/types/diagram";

export function ExportMermaidButton({ scope, depth }: DiagramRequest) {
  const [open, setOpen] = useState(false);
  const [copied, setCopied] = useState(false);
  const diagram = useDiagram();

  function handleOpen() {
    setOpen(true);
    setCopied(false);
    diagram.mutate({ scope, depth });
  }

  async function handleCopy() {
    if (!diagram.data) return;
    await navigator.clipboard.writeText(diagram.data.content);
    setCopied(true);
  }

  return (
    <>
      <button
        type="button"
        onClick={handleOpen}
        className="rounded-md border border-surface-border px-3 py-1.5 text-sm font-medium hover:bg-surface"
      >
        Export as Mermaid
      </button>
      <Modal open={open} onClose={() => setOpen(false)} title="Mermaid export">
        {diagram.isPending && <p className="text-sm text-muted-foreground">Generating diagram…</p>}
        {diagram.isError && (
          <p className="text-sm text-red-500">
            Failed to generate diagram:{" "}
            {diagram.error instanceof Error ? diagram.error.message : "unknown error"}
          </p>
        )}
        {diagram.data && (
          <div className="space-y-3">
            <div className="flex justify-end">
              <button
                type="button"
                onClick={handleCopy}
                className="rounded-md bg-accent px-3 py-1 text-xs font-medium text-white hover:opacity-90"
              >
                {copied ? "Copied!" : "Copy source"}
              </button>
            </div>
            <MermaidPreview content={diagram.data.content} />
            <pre className="max-h-48 overflow-auto rounded-md border border-surface-border bg-surface p-3 text-xs">
              {diagram.data.content}
            </pre>
          </div>
        )}
      </Modal>
    </>
  );
}
