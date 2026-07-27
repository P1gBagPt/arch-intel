// Mirrors GraphResponseDto (src/Api/ArchIntel.Api/Contracts/GraphResponseDto.cs) — GET /graph
export interface GraphNode {
  id: string;
  kind: string;
  name: string;
}

export interface GraphEdge {
  fromId: string;
  toId: string;
  type: string;
}

export interface GraphResponse {
  nodes: GraphNode[];
  edges: GraphEdge[];
  truncated: boolean;
}

export interface GraphFilters {
  scope?: string;
  depth?: number;
  kinds?: string[];
  full?: boolean;
}
