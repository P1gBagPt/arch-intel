import { Suspense } from "react";
import { PlannerPageClient } from "./PlannerPageClient";

export default function PlannerPage() {
  return (
    <Suspense fallback={<p className="text-sm text-muted-foreground">Loading planner…</p>}>
      <PlannerPageClient />
    </Suspense>
  );
}
