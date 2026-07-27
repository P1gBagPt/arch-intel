// Mirrors ServiceSummaryDto / ServiceDetailDto / NodeRefDto
// (src/Api/ArchIntel.Api/Contracts/ServiceSummaryDto.cs, ServiceDetailDto.cs, NodeRefDto.cs)
export interface ServiceSummary {
  id: string;
  name: string;
  kind: string;
  projectId: string;
  isHostedService: boolean;
}

export interface NodeRef {
  id: string;
  kind: string;
  name: string;
  relation: string | null;
}

// GET /services/:id
export interface ServiceDetail {
  id: string;
  name: string;
  kind: string;
  projectId: string;
  dependencies: NodeRef[];
  callers: NodeRef[];
  implements: NodeRef[];
  tests: NodeRef[];
}
