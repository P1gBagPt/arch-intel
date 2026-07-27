import type { ProjectSummary } from "@/types/project";

export function adaptProjectSummary(raw: ProjectSummary): ProjectSummary {
  return {
    id: raw.id,
    name: raw.name,
    path: raw.path,
    projectType: raw.projectType ?? null,
    layer: raw.layer ?? null,
    targetFramework: raw.targetFramework ?? null,
  };
}
