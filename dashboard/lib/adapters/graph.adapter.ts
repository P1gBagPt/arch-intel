import type { GraphResponse } from "@/types/graph";

// Raw API JSON already matches our internal shape field-for-field (GraphResponseDto is
// camelCase-serialized 1:1) — this seam exists so a future backend contract change only
// requires editing this file, not every component that reads graph data.
export function adaptGraphResponse(raw: GraphResponse): GraphResponse {
  return {
    nodes: raw.nodes ?? [],
    edges: raw.edges ?? [],
    truncated: raw.truncated ?? false,
  };
}
