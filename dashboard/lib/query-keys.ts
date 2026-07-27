import type { GraphFilters } from "@/types/graph";

// Namespaced by repoId even in single-repo Phase 2, so Phase 4's multi-repo cache
// partitioning (06-dashboard.md §11) is already the query key shape, not a later migration.
export const queryKeys = {
  projects: {
    all: (repoId: string) => ["repos", repoId, "projects"] as const,
  },
  services: {
    all: (repoId: string) => ["repos", repoId, "services"] as const,
    detail: (repoId: string, id: string) => ["repos", repoId, "services", id] as const,
  },
  graph: {
    all: (repoId: string) => ["repos", repoId, "graph"] as const,
    filtered: (repoId: string, filters: GraphFilters) =>
      ["repos", repoId, "graph", filters] as const,
  },
  impact: (repoId: string, targetId: string, maxDepth?: number) =>
    ["repos", repoId, "impact", targetId, maxDepth ?? null] as const,
  metrics: {
    all: (repoId: string) => ["repos", repoId, "metrics"] as const,
    coupling: (repoId: string) => ["repos", repoId, "metrics", "coupling"] as const,
    circularDependencies: (repoId: string) =>
      ["repos", repoId, "metrics", "circular-dependencies"] as const,
  },
};
