import type { TimelineEntry } from "@/hooks/useTimeline";

// Only ever shows a real added/removed project list (from diffing two GET /projects polls) —
// never fabricated per-class symbol names. When a re-scan changed class/interface/service counts
// without adding or removing a whole project, that's said explicitly rather than invented.
export function SnapshotDiffDrawer({ entry, onClose }: { entry: TimelineEntry; onClose: () => void }) {
  const diff = entry.projectDiff;
  const hasProjectChanges = !!diff && (diff.added.length > 0 || diff.removed.length > 0);

  return (
    <div className="w-72 shrink-0 space-y-3 border-l border-surface-border p-4">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-semibold">What changed</h2>
        <button type="button" onClick={onClose} className="text-xs text-muted-foreground hover:text-foreground">
          Close
        </button>
      </div>

      <p className="text-xs text-muted-foreground">{new Date(entry.timestamp).toLocaleString()}</p>

      {hasProjectChanges ? (
        <div className="space-y-3">
          {diff.added.length > 0 && (
            <div>
              <p className="text-xs font-medium text-coupling-stable">Added projects</p>
              <ul className="mt-1 space-y-0.5">
                {diff.added.map((p) => (
                  <li key={p.id} className="rounded bg-coupling-stable/10 px-2 py-1 text-xs">
                    {p.name}
                  </li>
                ))}
              </ul>
            </div>
          )}
          {diff.removed.length > 0 && (
            <div>
              <p className="text-xs font-medium text-coupling-high">Removed projects</p>
              <ul className="mt-1 space-y-0.5">
                {diff.removed.map((p) => (
                  <li key={p.id} className="rounded bg-coupling-high/10 px-2 py-1 text-xs">
                    {p.name}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      ) : (
        <p className="text-xs text-muted-foreground">
          No projects were added or removed — this change is class/interface/service counts moving
          within existing projects. There&apos;s no per-symbol diff endpoint on the backend today,
          so the specific classes involved can&apos;t be listed here.
        </p>
      )}
    </div>
  );
}
