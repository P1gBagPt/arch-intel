"use client";

import Link from "next/link";
import { useMemo, useState } from "react";
import { Badge } from "@/components/ui/Badge";
import { SearchInput } from "@/components/ui/SearchInput";
import { cn } from "@/lib/cn";
import type { ProjectSummary } from "@/types/project";

interface RepoTreeProps {
  projects: ProjectSummary[];
}

const UNGROUPED_LABEL = "Other";

export function RepoTree({ projects }: RepoTreeProps) {
  const [query, setQuery] = useState("");
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());

  const grouped = useMemo(() => {
    const q = query.trim().toLowerCase();
    const filtered = q
      ? projects.filter(
          (p) =>
            p.name.toLowerCase().includes(q) ||
            p.path.toLowerCase().includes(q) ||
            (p.layer ?? "").toLowerCase().includes(q),
        )
      : projects;

    const groups = new Map<string, ProjectSummary[]>();
    for (const project of filtered) {
      const key = project.layer ?? UNGROUPED_LABEL;
      const list = groups.get(key) ?? [];
      list.push(project);
      groups.set(key, list);
    }
    return [...groups.entries()].sort(([a], [b]) => a.localeCompare(b));
  }, [projects, query]);

  function toggle(group: string) {
    setCollapsed((prev) => {
      const next = new Set(prev);
      if (next.has(group)) next.delete(group);
      else next.add(group);
      return next;
    });
  }

  return (
    <div className="space-y-4">
      <SearchInput value={query} onChange={setQuery} placeholder="Filter projects…" />
      <div className="space-y-3">
        {grouped.map(([group, items]) => {
          const isCollapsed = collapsed.has(group);
          return (
            <div key={group} className="rounded-md border border-surface-border">
              <button
                type="button"
                onClick={() => toggle(group)}
                className="flex w-full items-center justify-between px-3 py-2 text-left text-sm font-semibold"
              >
                <span className="flex items-center gap-2">
                  <span className={cn("transition-transform", isCollapsed ? "-rotate-90" : "")}>
                    ▾
                  </span>
                  {group}
                </span>
                <Badge>{items.length}</Badge>
              </button>
              {!isCollapsed && (
                <ul className="divide-y divide-surface-border border-t border-surface-border">
                  {items.map((project) => (
                    <li key={project.id}>
                      <Link
                        href={`/graph/${encodeURIComponent(project.id)}`}
                        className="flex items-center justify-between px-4 py-2 text-sm hover:bg-surface"
                      >
                        <span className="flex flex-col">
                          <span className="font-medium">{project.name}</span>
                          <span className="text-xs text-muted-foreground">{project.path}</span>
                        </span>
                        {project.projectType && <Badge>{project.projectType}</Badge>}
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          );
        })}
        {grouped.length === 0 && (
          <p className="px-2 py-8 text-center text-sm text-muted-foreground">
            No projects match &ldquo;{query}&rdquo;.
          </p>
        )}
      </div>
    </div>
  );
}
