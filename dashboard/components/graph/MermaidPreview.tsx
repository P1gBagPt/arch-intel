"use client";

import { useEffect, useId, useState } from "react";

// Validates the returned Mermaid source actually renders (06-dashboard.md §11 "Mermaid export
// fidelity") rather than assuming the backend's output is always valid — surfaces a render
// error instead of silently showing nothing.
export function MermaidPreview({ content }: { content: string }) {
  const id = useId().replace(/:/g, "-");
  const [svg, setSvg] = useState<string | null>(null);
  const [renderError, setRenderError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    // Reset for the new `content` before the async render resolves, so a stale
    // svg/error from a previous diagram never flashes while the next one loads.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setSvg(null);
    setRenderError(null);

    import("mermaid").then(async ({ default: mermaid }) => {
      mermaid.initialize({ startOnLoad: false, theme: "neutral" });
      try {
        const { svg } = await mermaid.render(`mermaid-${id}`, content);
        if (!cancelled) setSvg(svg);
      } catch (err) {
        if (!cancelled) setRenderError(err instanceof Error ? err.message : "Failed to render diagram");
      }
    });

    return () => {
      cancelled = true;
    };
  }, [content, id]);

  if (renderError) {
    return (
      <p className="rounded-md border border-red-500/30 bg-red-500/10 p-3 text-sm text-red-500">
        Preview failed to render: {renderError}
      </p>
    );
  }

  if (!svg) {
    return <p className="text-sm text-muted-foreground">Rendering preview…</p>;
  }

  return <div className="overflow-auto rounded-md border border-surface-border bg-white p-3" dangerouslySetInnerHTML={{ __html: svg }} />;
}
