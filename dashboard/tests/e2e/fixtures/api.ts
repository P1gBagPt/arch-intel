import type { Page, Route } from "@playwright/test";
import type { ApiEnvelope } from "@/types/api";
import type { DiagramResponse } from "@/types/diagram";
import type { GraphResponse } from "@/types/graph";
import type { ImpactResponse } from "@/types/impact";
import type { CircularDependency, CouplingMetric } from "@/types/metrics";
import type { ProjectSummary } from "@/types/project";
import type { ServiceDetail, ServiceSummary } from "@/types/service";

// One small, deterministic "canonical sample architecture" (06-dashboard.md §10.3) — hand-built
// rather than captured scanner output, so ids are readable and every spec's assertions are exact
// rather than fragile against real scan hashes. Shape mirrors what the real ArchIntel.Api backend
// actually returned for the OrderRepository/IOrderRepository pattern during manual verification
// this session, so the fixture is a faithful stand-in, not a guess.
export const FIXTURE_REPO_ID = "default";

// Matches lib/api-client.ts's NEXT_PUBLIC_API_URL fallback — nothing needs to actually be
// listening here since every request to it is intercepted by mockApi() below.
export const API_ORIGIN = "http://localhost:5219";

export const PROJECTS: ProjectSummary[] = [
  { id: "proj-domain", name: "SampleErp.Domain", path: "Domain/SampleErp.Domain.csproj", projectType: null, layer: "Domain", targetFramework: null },
  { id: "proj-application", name: "SampleErp.Application", path: "Application/SampleErp.Application.csproj", projectType: null, layer: "Application", targetFramework: null },
  { id: "proj-infrastructure", name: "SampleErp.Infrastructure", path: "Infrastructure/SampleErp.Infrastructure.csproj", projectType: null, layer: "Infrastructure", targetFramework: null },
];

export const SERVICES: ServiceSummary[] = [
  { id: "svc-handler", name: "CreateOrderCommandHandler", kind: "MediatRHandler", projectId: "proj-application", isHostedService: false },
];

export const GRAPH: GraphResponse = {
  nodes: [
    { id: "svc-handler", kind: "MediatRHandler", name: "CreateOrderCommandHandler" },
    { id: "iorder-repository", kind: "Interface", name: "IOrderRepository" },
    { id: "order-repository", kind: "Repository", name: "OrderRepository" },
    { id: "fake-order-repository", kind: "Repository", name: "FakeOrderRepository" },
    { id: "handler-tests", kind: "TestClass", name: "CreateOrderCommandHandlerTests" },
  ],
  edges: [
    { fromId: "svc-handler", toId: "iorder-repository", type: "Injects" },
    { fromId: "order-repository", toId: "iorder-repository", type: "Implements" },
    { fromId: "fake-order-repository", toId: "iorder-repository", type: "Implements" },
  ],
  truncated: false,
};

export const SERVICE_DETAIL_BY_ID: Record<string, ServiceDetail> = {
  "svc-handler": {
    id: "svc-handler",
    name: "CreateOrderCommandHandler",
    kind: "MediatRHandler",
    projectId: "proj-application",
    dependencies: [{ id: "iorder-repository", kind: "Interface", name: "IOrderRepository", relation: "Injects" }],
    callers: [],
    implements: [],
    tests: [{ id: "handler-tests", kind: "TestClass", name: "CreateOrderCommandHandlerTests", relation: null }],
  },
  "order-repository": {
    id: "order-repository",
    name: "OrderRepository",
    kind: "Repository",
    projectId: "proj-infrastructure",
    dependencies: [],
    callers: [{ id: "iorder-repository", kind: "Interface", name: "IOrderRepository", relation: "Owns" }],
    implements: [{ id: "iorder-repository", kind: "Interface", name: "IOrderRepository", relation: null }],
    tests: [],
  },
  "iorder-repository": {
    id: "iorder-repository",
    name: "IOrderRepository",
    kind: "Interface",
    projectId: "proj-domain",
    dependencies: [],
    callers: [
      { id: "order-repository", kind: "Repository", name: "OrderRepository", relation: "Owns" },
      { id: "fake-order-repository", kind: "Repository", name: "FakeOrderRepository", relation: "Owns" },
      { id: "svc-handler", kind: "MediatRHandler", name: "CreateOrderCommandHandler", relation: "Injects" },
    ],
    implements: [],
    tests: [],
  },
};

export const IMPACT_BY_TARGET_ID: Record<string, ImpactResponse> = {
  "iorder-repository": {
    targetId: "iorder-repository",
    targetName: "IOrderRepository",
    affected: [
      { id: "order-repository", kind: "Repository", name: "OrderRepository", relation: "Implements", depth: 1, riskLevel: "Low" },
      { id: "fake-order-repository", kind: "Repository", name: "FakeOrderRepository", relation: "Implements", depth: 1, riskLevel: "Low" },
      { id: "svc-handler", kind: "MediatRHandler", name: "CreateOrderCommandHandler", relation: "Injects", depth: 1, riskLevel: "Low" },
    ],
    summary: { totalAffected: 3, byKind: { Repository: 2, MediatRHandler: 1 } },
  },
};

export const COUPLING: CouplingMetric[] = [
  { projectId: "proj-domain", projectName: "SampleErp.Domain", afferentCoupling: 8, efferentCoupling: 2, instability: 0.2, band: "Green" },
  { projectId: "proj-application", projectName: "SampleErp.Application", afferentCoupling: 4, efferentCoupling: 4, instability: 0.5, band: "Yellow" },
  { projectId: "proj-infrastructure", projectName: "SampleErp.Infrastructure", afferentCoupling: 0, efferentCoupling: 5, instability: 1, band: "Red" },
];

export const CIRCULAR_DEPENDENCIES: CircularDependency[] = [
  { cycle: ["proj-application", "proj-infrastructure", "proj-application"], length: 2 },
];

export const DIAGRAM: DiagramResponse = {
  format: "mermaid",
  content: 'graph TD\n  iorder_repository["IOrderRepository"] -->|Implements| order_repository["OrderRepository"]',
};

function envelope<T>(data: T): ApiEnvelope<T> {
  return { data, page: null };
}

function json(route: Route, body: unknown, status = 200) {
  return route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
}

// Registers a single catch-all route over the API's origin (the dashboard's api-client always
// targets an absolute NEXT_PUBLIC_API_URL, not a same-origin path — see lib/api-client.ts — so
// this must match that origin, not the Next dev server's).
export async function mockApi(page: Page, apiOrigin: string) {
  await page.route(`${apiOrigin}/api/v1/repos/*/projects*`, (route) => json(route, envelope(PROJECTS)));
  // Trailing `*` (not `/*`) matches an optional `?query=string` without also matching
  // `/services/{id}` — glob `*` doesn't cross the `/` the detail route below needs.
  await page.route(`${apiOrigin}/api/v1/repos/*/services*`, (route) => json(route, envelope(SERVICES)));

  await page.route(`${apiOrigin}/api/v1/repos/*/services/*`, (route) => {
    const url = new URL(route.request().url());
    const id = url.pathname.split("/").pop()!;
    const detail = SERVICE_DETAIL_BY_ID[id];
    if (!detail) return json(route, { title: "Graph node not found" }, 404);
    return json(route, envelope(detail));
  });

  await page.route(`${apiOrigin}/api/v1/repos/*/graph*`, (route) => json(route, envelope(GRAPH)));

  await page.route(`${apiOrigin}/api/v1/repos/*/impact*`, (route) => {
    const url = new URL(route.request().url());
    const nodeId = url.searchParams.get("nodeId") ?? "";
    const impact = IMPACT_BY_TARGET_ID[nodeId];
    if (!impact) return json(route, { title: "Graph node not found" }, 404);
    return json(route, envelope(impact));
  });

  await page.route(`${apiOrigin}/api/v1/repos/*/diagram`, (route) => json(route, envelope(DIAGRAM)));

  await page.route(`${apiOrigin}/api/v1/repos/*/metrics/coupling`, (route) => json(route, envelope(COUPLING)));
  await page.route(`${apiOrigin}/api/v1/repos/*/metrics/circular-dependencies`, (route) =>
    json(route, envelope(CIRCULAR_DEPENDENCIES)),
  );
}
