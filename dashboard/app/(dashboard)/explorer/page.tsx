"use client";

import { RepoTree } from "@/components/domain/RepoTree";
import { useProjects } from "@/hooks/useProjects";

export default function ExplorerPage() {
  const { data: projects, isLoading, isError, error } = useProjects();

  if (isLoading) {
    return <p className="text-sm text-muted-foreground">Loading projects…</p>;
  }

  if (isError) {
    return (
      <p className="text-sm text-red-500">
        Failed to load projects: {error instanceof Error ? error.message : "unknown error"}
      </p>
    );
  }

  return (
    <div className="mx-auto max-w-3xl space-y-4">
      <h1 className="text-xl font-semibold">Repository Explorer</h1>
      <RepoTree projects={projects ?? []} />
    </div>
  );
}
