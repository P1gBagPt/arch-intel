// Mirrors ProjectSummaryDto (src/Api/ArchIntel.Api/Contracts/ProjectSummaryDto.cs) — GET /projects
export interface ProjectSummary {
  id: string;
  name: string;
  path: string;
  projectType: string | null;
  layer: string | null;
  targetFramework: string | null;
}
