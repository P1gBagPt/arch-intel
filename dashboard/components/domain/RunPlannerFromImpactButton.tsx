import Link from "next/link";

// 06-dashboard.md §4.4's cross-link: pre-fills the AI Planner input with "Implement changes to
// <target>" rather than passing targetId as scopeProjectIds — the impact target is usually a
// class/interface/entity node, not a project id, and ImplementationPlanRequest.ScopeProjectIds is
// only ever matched against project ids server-side (PlaceholderPlanningService.GeneratePlanAsync).
export function RunPlannerFromImpactButton({ targetName }: { targetName: string }) {
  const prompt = `Implement changes to ${targetName}`;
  const href = `/planner?kind=implementation-plan&prompt=${encodeURIComponent(prompt)}`;

  return (
    <Link
      href={href}
      className="shrink-0 rounded-md border border-surface-border px-3 py-1.5 text-sm font-medium hover:bg-surface"
    >
      Plan this change
    </Link>
  );
}
