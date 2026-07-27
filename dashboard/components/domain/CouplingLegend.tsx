import { COUPLING_BAND_STYLES } from "@/lib/constants/coupling-scale";

// Color is never the only signal (WCAG "don't rely on color alone", 06-dashboard.md §5.4) —
// every band also gets its text label here and in the grid/table cells themselves.
export function CouplingLegend() {
  return (
    <div className="flex items-center gap-4 text-xs text-muted-foreground">
      {(Object.entries(COUPLING_BAND_STYLES) as [string, (typeof COUPLING_BAND_STYLES)[keyof typeof COUPLING_BAND_STYLES]][]).map(
        ([band, style]) => (
          <span key={band} className="flex items-center gap-1.5">
            <span className={`h-3 w-3 rounded-full border-2 ${style.border} ${style.bg}`} />
            {style.label}
          </span>
        ),
      )}
    </div>
  );
}
