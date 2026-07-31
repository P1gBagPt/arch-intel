import type { CouplingBand } from "@/types/metrics";

// Matches the real backend's exact band literals (GraphMetricsComputer.BandFor in
// src/Api/ArchIntel.Api/Analysis/GraphMetricsComputer.cs) — "Green"/"Yellow"/"Red", not the
// "Stable"/"Moderate"/"Highly coupled" prose the plan doc uses; those are display labels only,
// kept here so the heatmap grid and any inline coupling badge (06-dashboard.md §5.4) agree.
export const COUPLING_BAND_STYLES: Record<CouplingBand, { border: string; text: string; bg: string; label: string }> = {
  Green: { border: "border-coupling-stable", text: "text-coupling-stable", bg: "bg-coupling-stable/10", label: "Stable" },
  Yellow: { border: "border-coupling-moderate", text: "text-coupling-moderate", bg: "bg-coupling-moderate/10", label: "Moderate" },
  Red: { border: "border-coupling-high", text: "text-coupling-high", bg: "bg-coupling-high/10", label: "Highly coupled" },
};

export function couplingBandStyle(band: string) {
  return COUPLING_BAND_STYLES[band as CouplingBand] ?? COUPLING_BAND_STYLES.Green;
}

// SVG fill/stroke attributes accept var() directly (same pattern as TimelineTrendChart's
// stroke="var(--accent)") — needed for the treemap's canvas-drawn cells, which can't use
// Tailwind's bg-coupling-* utility classes the way the grid/table cells above do.
export const COUPLING_BAND_COLOR_VAR: Record<CouplingBand, string> = {
  Green: "var(--coupling-stable)",
  Yellow: "var(--coupling-moderate)",
  Red: "var(--coupling-high)",
};

export function couplingBandColorVar(band: string) {
  return COUPLING_BAND_COLOR_VAR[band as CouplingBand] ?? COUPLING_BAND_COLOR_VAR.Green;
}
