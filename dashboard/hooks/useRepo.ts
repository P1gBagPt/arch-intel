// Phase 2 is single-repo (06-dashboard.md §2 Phase 2, §11 "Multi-repo retrofitting risk") —
// every view already reads the active repo through this hook rather than a hardcoded literal,
// so Phase 4's real repo switcher only needs to replace this implementation, not every call site.
const DEFAULT_REPO_ID = process.env.NEXT_PUBLIC_DEFAULT_REPO_ID ?? "default";

export function useRepo() {
  return { repoId: DEFAULT_REPO_ID };
}
