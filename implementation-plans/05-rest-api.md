# 05 — REST API: Implementation Plan

> Component 5 of the Architecture Intelligence Platform (see root `README.md`). This document plans the ASP.NET Core Minimal API that powers the Next.js dashboard, exposes SignalR-based live updates, and (from Phase 3 onward) fronts the AI planning service shared with the MCP Server.
>
> Cross-referenced documents (assumed contracts, not redesigned here):
> - `02-graph-store.md` — the Graph Store reader/query contract (`IGraphReader` or equivalent), schema for nodes/edges/snapshots, SQLite (Phase 1-3) and PostgreSQL/Neo4j (Phase 4+) backends.
> - `03-incremental-watcher.md` — the watcher process that detects file changes, rebuilds affected graph nodes, and raises change notifications that this API must relay over SignalR.
> - `04-mcp-server.md` — the MCP Server, which shares the AI Planning Service (implementation plan generation, architecture analysis) with this REST API. Both are thin transport layers over the same planning core.

---

## 1. Overview & Responsibilities

The REST API is the HTTP transport layer between the Architecture Graph Store and the Next.js Dashboard. It does not own architectural knowledge — it is a **read-mostly facade** over the Graph Store, plus a **write/trigger facade** over long-running AI operations (implementation plan generation, diagram generation, architecture analysis) that are executed by a shared Planning Service.

Responsibilities:

* Expose graph data (projects, services, dependency graph, metrics, impact) as versioned, paginated JSON over HTTP.
* Accept requests that trigger AI-driven generation (implementation plans, architecture analysis) and Mermaid diagram export.
* Push live updates to connected dashboard clients via SignalR whenever the Incremental Watcher mutates the graph.
* From Phase 4: enforce authentication/authorization, scope every request to a repository (and, later, a team/tenant), and expose historical snapshots for the Architecture Timeline view.

Explicitly **not** the responsibility of this component:

* Scanning source code or building the graph (Architecture Scanner / Incremental Watcher).
* Owning graph storage or query execution internals (Graph Store).
* Owning LLM prompt construction or plan synthesis logic (Planning Service, shared with MCP Server) — the REST API only calls into it and shapes the HTTP contract around its result.

### Consumers

* **Next.js Dashboard** (primary, only consumer through Phase 3). Uses TanStack Query for GET endpoints and a SignalR client (`@microsoft/signalr`) for live updates.
* **Phase 4**: potentially third-party integrations (CI systems, Slack bots, other internal tools) once auth exists — treated as a secondary consumer, not designed for in depth here.

### Relationship to the MCP Server

The MCP Server (`04-mcp-server.md`) and the REST API are two transports over the same capabilities. Both call into a shared `PatternVision.Modules.Architecture.Planning` core library (see §7) that exposes:

```csharp
Task<ImplementationPlanResult> GeneratePlanAsync(ImplementationPlanRequest request, CancellationToken ct);
Task<ArchitectureAnalysisResult> AnalyzeAsync(ArchitectureAnalysisRequest request, CancellationToken ct);
```

Neither the REST API nor the MCP Server re-implements plan synthesis; each maps its own transport-specific request/response shape onto these shared contracts. This document treats the Planning Service's internal behavior as out of scope and focuses on how the REST API surfaces it.

---

## 2. Phase-by-Phase Scope

### Phase 1 — Minimal read API over SQLite (local dev tool, no auth)

* Stand up the ASP.NET Core Minimal API host (`arch api` or `dotnet run` locally, no separate deployment).
* Implement `GET /projects`, `GET /services`, `GET /graph` directly against the Graph Store reader contract (SQLite-backed).
* No authentication — this runs as a local developer tool, bound to `localhost` by default.
* No SignalR yet (Incremental Watcher does not exist yet in Phase 1).
* Basic OpenAPI/Swagger for local exploration.
* Problem Details error handling scaffolding put in place early so later phases don't retrofit it.

### Phase 2 — Full dashboard-supporting endpoint set

* Add `GET /impact`, `GET /metrics` (first cut — enough for Coupling Heatmap and basic stats).
* Add `POST /diagram` (Mermaid export) to support the dashboard's diagram/export actions and `arch diagram`.
* Flesh out `GET /graph` with filtering/pagination sufficient for the Dependency Graph and Repository Explorer views.
* Add `GET /services/{id}` (Service Explorer detail: dependencies, callers, implementations, tests, interfaces).
* Introduce API versioning (`/api/v1`) before the surface grows further.
* CORS configured for the dashboard's dev/prod origins.

### Phase 3 — AI planner, live updates, refined analysis endpoints

* `POST /implementation-plan` and `POST /architecture-analysis`, both delegating to the shared Planning Service.
* SignalR hub (`/hubs/architecture`) added; Incremental Watcher publishes change events that the API relays to connected dashboard clients (`graph:updated`, `metrics:updated`, `scan:progress`).
* `GET /impact` upgraded to full impact analysis (transitive impact, confidence/risk annotations) backing the Impact Analysis view.
* `GET /metrics` expanded with coupling metrics (afferent/efferent coupling, instability) for the Coupling Heatmap; add `GET /metrics/circular-dependencies`.
* Background polling/queueing pattern for long-running AI requests (see §3.6) since plan generation may exceed typical HTTP timeouts.

### Phase 4 — Auth, multi-repo, collaboration, deployment hardening

* Authentication via Better Auth session cookies / bearer tokens, with GitHub OAuth and Microsoft Entra ID as identity providers.
* Every endpoint becomes repository-scoped (`/api/v1/repos/{repoId}/...`), with authorization checks against team membership/roles.
* `GET /snapshots` and `GET /snapshots/{id}/diff` for the Architecture Timeline, backed by Graph Store historical snapshot support.
* Team collaboration: shared graphs, per-repo roles (Owner/Maintainer/Viewer), invitation endpoints.
* Production deployment to Azure App Service / Docker / Railway / Fly.io, with health checks, structured logging, and rate limiting.
* Architecture quality scoring endpoint (`GET /quality-score`) feeding a roadmap "Secondary Goal."

---

## 3. Technical Design

### 3.1 Minimal API structure

The API is built on ASP.NET Core Minimal APIs (no MVC controllers), organized as **endpoint group modules** rather than one giant `Program.cs`. Each feature area is a static class exposing a `MapXyz(this IEndpointRouteBuilder app)` extension method.

```
Program.cs
  -> builder.Services.AddArchitectureApi(...)
  -> app.MapProjectsEndpoints();
  -> app.MapServicesEndpoints();
  -> app.MapGraphEndpoints();
  -> app.MapImpactEndpoints();
  -> app.MapMetricsEndpoints();
  -> app.MapDiagramEndpoints();
  -> app.MapPlanningEndpoints();      // /implementation-plan, /architecture-analysis
  -> app.MapSnapshotsEndpoints();     // Phase 4
  -> app.MapHub<ArchitectureHub>("/hubs/architecture"); // Phase 3+
```

Each endpoint group:

* Declares a `RouteGroupBuilder` with a shared prefix and tags (for OpenAPI grouping).
* Applies shared filters (validation, repo-scope resolution) via `.AddEndpointFilter<T>()`.
* Has its own DTOs, kept in a `Contracts/` folder colocated with the endpoint group, to avoid a shared "God DTO" namespace.

### 3.2 Endpoint grouping & versioning

* All routes are prefixed `/api/v1` from Phase 2 onward (Phase 1 may ship unprefixed `/projects` etc. for simplicity, but the plan is to alias both during the Phase 1→2 transition and deprecate the unprefixed routes by Phase 2 completion).
* Versioning strategy: URL segment versioning (`/api/v1`, `/api/v2` if ever needed), not header-based — simplest for a dashboard-only consumer and easiest to reason about in Next.js fetch clients.
* Phase 4 adds repo scoping as a route segment: `/api/v1/repos/{repoId}/graph`, etc. Endpoints without a natural repo scope (auth, org-level settings) stay unscoped.
* Route groups are tagged for OpenAPI (`Projects`, `Services`, `Graph`, `Impact`, `Metrics`, `Diagram`, `Planning`, `Snapshots`, `Realtime`) so the generated Swagger UI / client SDK is navigable.

### 3.3 DTO strategy

* DTOs are **immutable records**, separate from Graph Store domain entities. The Graph Store reader contract (`02-graph-store.md`) returns its own domain model (e.g., `GraphNode`, `GraphEdge`, `ProjectNode`); the REST API maps these to API-facing DTOs via small `static class XyzMapper` classes.
* Rationale: keeps the HTTP contract stable even if the Graph Store's internal schema evolves (e.g., SQLite → PostgreSQL/Neo4j in Phase 4), and lets the API version its shape independently.
* Shared primitives:

```csharp
public sealed record NodeRef(string Id, string Kind, string Name);
public sealed record PageInfo(int Page, int PageSize, int TotalCount, bool HasNextPage);
public sealed record ApiEnvelope<T>(T Data, PageInfo? Page = null, string? RequestId = null);
```

* Every collection-returning endpoint responds inside an `ApiEnvelope<T>` so pagination metadata has one consistent home and clients don't need per-endpoint parsing logic.

### 3.4 Error handling / Problem Details

* Uses `Microsoft.AspNetCore.Http.HttpResults` typed results (`Results<Ok<T>, NotFound, ValidationProblem>`) so error shapes are visible in the OpenAPI schema, not just in behavior.
* Global exception handling via `app.UseExceptionHandler()` configured to emit **RFC 9457 Problem Details** (`application/problem+json`) for unhandled exceptions, using `AddProblemDetails()` with a custom `CustomizeProblemDetails` callback that adds a `traceId` and, where available, a `graphNodeId` extension for graph-related errors.
* Standard problem types used across the API:

```json
{
  "type": "https://arch-intel.dev/problems/node-not-found",
  "title": "Graph node not found",
  "status": 404,
  "detail": "No node with id 'svc_order_service' exists in the current graph snapshot.",
  "instance": "/api/v1/services/svc_order_service",
  "traceId": "00-4bf92f...-01"
}
```

* Validation errors (bad query params, malformed POST bodies) go through an endpoint filter that runs `FluentValidation` (or DataAnnotations for simple cases) and returns `ValidationProblem` (`400`) with per-field error arrays.
* Long-running AI operations that fail (LLM timeout, planning service unavailable) return a distinct problem type `.../problems/planning-service-unavailable` with `status: 503` and a `Retry-After` header, so the dashboard can distinguish "your input was bad" from "try again later."

### 3.5 Pagination for large graphs

Full dependency graphs can have thousands of nodes/edges (a stated example: "2,350 classes"). Two complementary strategies:

1. **Cursor-based pagination** for flat list endpoints (`/projects`, `/services`, `/metrics` rows): `?cursor=<opaque>&limit=100`, response includes `nextCursor`. Chosen over offset pagination because the underlying graph can mutate between watcher-triggered rebuilds (Phase 3+), and cursor pagination degrades more gracefully under concurrent writes than offset-based paging.
2. **Depth/scope-limited subgraph queries** for `/graph`: rather than paginating a graph response (which breaks visual coherence), the endpoint requires a `scope` (e.g., a project or service id) and a `depth` (default 2, max configurable), returning a bounded subgraph. A `full=true` escape hatch is allowed but discouraged in the dashboard UI and rate-limited in Phase 4. This maps directly onto the Graph Store's expected "bounded traversal" query capability from `02-graph-store.md`.

Both approaches share a `PageInfo`/`GraphScope` envelope so the frontend has one predictable mental model per endpoint category.

### 3.6 Long-running AI operations pattern (Phase 3+)

`POST /implementation-plan` and `POST /architecture-analysis` call into an LLM-backed Planning Service and may take longer than is comfortable for a synchronous HTTP request/response (tens of seconds).

Design:

* Initial POST returns `202 Accepted` immediately with a `jobId` and a `Location` header pointing at `GET /jobs/{jobId}`.
* A lightweight in-memory (Phase 3) / durable (Phase 4, e.g., backed by the same SQLite/Postgres store) job table tracks `Pending -> Running -> Completed|Failed`.
* The dashboard either polls `GET /jobs/{jobId}` (TanStack Query with `refetchInterval`) or subscribes to a SignalR group `job:{jobId}` for a push-based `job:completed` event — the plan favors SignalR once the hub exists (Phase 3), falling back to polling only if the client hasn't got a socket connection.
* This same async job pattern is intentionally shared conceptually with how the MCP Server exposes `implementation_plan()` (per `04-mcp-server.md`), though each transport adapts it to its own idioms (MCP tool call polling vs REST job endpoint).

### 3.7 SignalR hub design (summary — full detail in §5)

* Single hub, `ArchitectureHub`, mounted at `/hubs/architecture`.
* Clients join groups per repository (`repo:{repoId}`) once multi-repo support lands (Phase 4); pre-Phase-4 there is a single implicit global group since only one repo/graph is tracked locally.
* Server-to-client events only in Phase 3 (`graph:updated`, `metrics:updated`, `scan:progress`, `job:completed`); no client-to-server RPC methods are needed initially beyond `JoinRepo`/`LeaveRepo` (Phase 4).

---

## 4. Endpoint Reference

Base path shown without version prefix for Phase 1 examples, with `/api/v1` from Phase 2 onward. Phase 4 adds `/repos/{repoId}` scoping to all of these (shown in §6).

### 4.1 `GET /projects` (Phase 1)

Lists all projects discovered by the scanner. Backed by `IGraphReader.GetProjectsAsync()` (Graph Store contract, `02-graph-store.md`).

Request:
```
GET /api/v1/projects?cursor=&limit=50&type=Business
```

Response `200`:
```json
{
  "data": [
    {
      "id": "proj_business_orders",
      "name": "PatternVision.Modules.Orders.Business",
      "type": "Business",
      "path": "src/Modules/Orders/PatternVision.Modules.Orders.Business",
      "classCount": 42,
      "interfaceCount": 11,
      "dependsOnProjectIds": ["proj_domain_orders", "proj_common"]
    }
  ],
  "page": { "page": 1, "pageSize": 50, "totalCount": 63, "hasNextPage": true },
  "requestId": "a1e2c3"
}
```

### 4.2 `GET /services` (Phase 1)

Lists discovered services (classes registered in DI as services, MediatR handlers treated as service-like, background workers). Backed by `IGraphReader.GetServicesAsync()`.

```json
{
  "data": [
    {
      "id": "svc_order_service",
      "name": "OrderService",
      "kind": "Service",
      "projectId": "proj_business_orders",
      "implementsInterfaceIds": ["iface_iorder_service"],
      "isHostedService": false
    }
  ],
  "page": { "page": 1, "pageSize": 50, "totalCount": 128, "hasNextPage": true }
}
```

### 4.3 `GET /services/{id}` (Phase 2)

Service Explorer detail view. Backed by `IGraphReader.GetServiceDetailAsync(id)` which internally composes several Graph Store queries (dependencies, inbound callers, implementations, associated tests).

```json
{
  "data": {
    "id": "svc_order_service",
    "name": "OrderService",
    "projectId": "proj_business_orders",
    "dependencies": [
      { "id": "repo_order_repository", "kind": "Repository", "name": "OrderRepository" }
    ],
    "callers": [
      { "id": "ctrl_order_controller", "kind": "Endpoint", "name": "OrderController.Create" }
    ],
    "implements": [
      { "id": "iface_iorder_service", "kind": "Interface", "name": "IOrderService" }
    ],
    "tests": [
      { "id": "test_order_service_tests", "kind": "TestClass", "name": "OrderServiceTests" }
    ]
  }
}
```

Errors: `404` with `.../problems/node-not-found` if `id` doesn't exist in the current graph.

### 4.4 `GET /graph` (Phase 1 minimal, Phase 2 full)

Bounded subgraph query for the Dependency Graph view. Backed by `IGraphReader.GetSubgraphAsync(scope, depth, kindFilter)`.

Phase 1: returns the whole graph unfiltered (acceptable at small scale, local dev only).

Phase 2 request:
```
GET /api/v1/graph?scope=proj_business_orders&depth=2&kinds=Project,Service,Interface
```

Response `200`:
```json
{
  "data": {
    "nodes": [
      { "id": "proj_business_orders", "kind": "Project", "name": "PatternVision.Modules.Orders.Business" },
      { "id": "svc_order_service", "kind": "Service", "name": "OrderService" },
      { "id": "iface_iorder_service", "kind": "Interface", "name": "IOrderService" }
    ],
    "edges": [
      { "fromId": "svc_order_service", "toId": "iface_iorder_service", "type": "Implements" },
      { "fromId": "proj_business_orders", "toId": "svc_order_service", "type": "Contains" }
    ],
    "truncated": false
  }
}
```

`truncated: true` signals the client that `depth`/`scope` hit the server-side node cap (configurable, default 2000 nodes) and should narrow its query.

### 4.5 `GET /impact` (Phase 2 basic, Phase 3 refined)

Impact Analysis view: "select a class, highlight everything affected." Backed by `IGraphReader.GetImpactAsync(nodeId, direction)` (Phase 2, direct dependents only) upgraded in Phase 3 to `IGraphReader.GetTransitiveImpactAsync(nodeId, maxDepth)` plus risk annotation from the Planning Service's static heuristics (not full LLM analysis — that's `/architecture-analysis`).

Phase 2 request/response:
```
GET /api/v1/impact?nodeId=class_model_version
```
```json
{
  "data": {
    "targetId": "class_model_version",
    "targetName": "ModelVersion",
    "affected": [
      { "id": "ctrl_model_controller", "kind": "Endpoint", "name": "ModelController", "relation": "References" },
      { "id": "repo_model_repository", "kind": "Repository", "name": "ModelRepository", "relation": "References" }
    ]
  }
}
```

Phase 3 response adds transitive depth and risk:
```json
{
  "data": {
    "targetId": "class_model_version",
    "targetName": "ModelVersion",
    "affected": [
      {
        "id": "ctrl_model_controller", "kind": "Endpoint", "name": "ModelController",
        "relation": "References", "depth": 1, "riskLevel": "Low"
      },
      {
        "id": "worker_model_sync_worker", "kind": "BackgroundWorker", "name": "ModelSyncWorker",
        "relation": "Uses", "depth": 2, "riskLevel": "Medium"
      }
    ],
    "summary": { "totalAffected": 14, "byKind": { "Endpoint": 2, "Repository": 1, "Validator": 3, "Test": 6, "BackgroundWorker": 2 } }
  }
}
```

### 4.6 `GET /metrics` (Phase 2 basic, Phase 3 expanded)

Backs the Coupling Heatmap and general architecture stats. Backed by `IGraphReader.GetMetricsAsync()` / `GetCouplingMetricsAsync()`.

Phase 2:
```json
{
  "data": {
    "totalProjects": 63,
    "totalClasses": 2350,
    "totalInterfaces": 340,
    "totalServices": 128,
    "generatedAtUtc": "2026-07-24T10:00:00Z"
  }
}
```

Phase 3 (`GET /api/v1/metrics/coupling`):
```json
{
  "data": [
    {
      "projectId": "proj_business_orders",
      "afferentCoupling": 14,
      "efferentCoupling": 6,
      "instability": 0.30,
      "band": "Green"
    },
    {
      "projectId": "proj_infrastructure_orders",
      "afferentCoupling": 3,
      "efferentCoupling": 22,
      "instability": 0.88,
      "band": "Red"
    }
  ]
}
```

`band` is a server-computed classification (`Green`/`Yellow`/`Red`) so the dashboard doesn't reimplement the thresholding logic — thresholds configurable via app settings.

Additional Phase 3 endpoint: `GET /api/v1/metrics/circular-dependencies` returning detected cycles:
```json
{
  "data": [
    { "cycle": ["proj_a", "proj_b", "proj_c", "proj_a"], "length": 3 }
  ]
}
```

### 4.7 `POST /diagram` (Phase 2)

Mermaid export for a scoped subgraph. Delegates to `IGraphReader.GetSubgraphAsync` then a `MermaidDiagramRenderer` (API-owned, not a Graph Store concern).

Request:
```json
{
  "scope": "proj_business_orders",
  "depth": 2,
  "kinds": ["Project", "Service", "Interface"],
  "format": "mermaid"
}
```

Response `200`:
```json
{
  "data": {
    "format": "mermaid",
    "content": "graph TD\n  proj_business_orders[\"PatternVision.Modules.Orders.Business\"] --> svc_order_service[\"OrderService\"]\n  svc_order_service --> iface_iorder_service[\"IOrderService\"]"
  }
}
```

`format` is extensible (`mermaid` only in Phase 2; `plantuml`/`svg` are noted as open questions, see §10).

### 4.8 `POST /implementation-plan` (Phase 3)

Delegates to the shared Planning Service's `GeneratePlanAsync`. Returns `202` per the async job pattern in §3.6.

Request:
```json
{
  "prompt": "Implement Archive Model",
  "scopeProjectIds": ["proj_business_models"]
}
```

Response `202`:
```json
{
  "data": { "jobId": "job_7f3a", "status": "Pending" }
}
```
Headers: `Location: /api/v1/jobs/job_7f3a`

`GET /api/v1/jobs/job_7f3a` once completed:
```json
{
  "data": {
    "jobId": "job_7f3a",
    "status": "Completed",
    "result": {
      "affectedProjects": ["proj_business_models", "proj_infrastructure_models"],
      "newFiles": ["ArchiveModelCommand.cs", "ArchiveModelHandler.cs"],
      "modifiedServices": ["ModelService"],
      "databaseChanges": ["Add column Models.ArchivedAtUtc"],
      "testsRequired": ["ArchiveModelHandlerTests"],
      "riskLevel": "Medium",
      "estimatedEffort": "4-6 hours"
    }
  }
}
```

This response shape mirrors the MCP Server's `implementation_plan()` tool result (`04-mcp-server.md`) field-for-field, since both surface the same `ImplementationPlanResult` contract from the shared Planning Service.

### 4.9 `POST /architecture-analysis` (Phase 3)

Delegates to `AnalyzeAsync`. Also async-job-shaped.

Request:
```json
{
  "question": "What would break if we removed IOrderService?",
  "scopeNodeIds": ["iface_iorder_service"]
}
```

`GET /api/v1/jobs/{jobId}` completed result:
```json
{
  "data": {
    "jobId": "job_9b21",
    "status": "Completed",
    "result": {
      "summary": "Removing IOrderService would break 3 direct implementations and 9 downstream consumers...",
      "affectedNodeIds": ["svc_order_service", "ctrl_order_controller"],
      "recommendations": ["Introduce a facade before removal", "Migrate consumers incrementally"]
    }
  }
}
```

### 4.10 `GET /jobs/{jobId}` (Phase 3, supporting endpoint)

Not in the README's example list but required to support the async pattern in §3.6.

```json
{ "data": { "jobId": "job_7f3a", "status": "Running", "progressPercent": 40 } }
```

Statuses: `Pending`, `Running`, `Completed`, `Failed`. `Failed` includes a `problem` object matching §3.4's Problem Details shape.

### 4.11 `GET /snapshots` and `GET /snapshots/{id}` (Phase 4, proposed)

Backs the Architecture Timeline view ("Today: 2,350 classes, Yesterday: 2,322 classes, Changes: +28 classes..."). Backed by the Graph Store's historical snapshot capability (`02-graph-store.md`, Phase 4 extension).

```
GET /api/v1/repos/{repoId}/snapshots?from=2026-07-01&to=2026-07-24
```
```json
{
  "data": [
    { "id": "snap_20260724", "takenAtUtc": "2026-07-24T00:00:00Z", "classCount": 2350, "projectCount": 63, "interfaceCount": 340 },
    { "id": "snap_20260723", "takenAtUtc": "2026-07-23T00:00:00Z", "classCount": 2322, "projectCount": 60, "interfaceCount": 341 }
  ]
}
```

`GET /api/v1/repos/{repoId}/snapshots/{id}/diff?against={otherId}` (proposed):
```json
{
  "data": {
    "classesAdded": 28,
    "classesRemoved": 0,
    "projectsAdded": 3,
    "projectsRemoved": 0,
    "interfacesRemoved": 1,
    "addedNodeIds": ["class_archive_model"],
    "removedNodeIds": ["iface_ilegacy_repository"]
  }
}
```

### 4.12 `GET /quality-score` (Phase 4, proposed)

Supports the roadmap's "Architecture quality scoring." Composed from existing coupling/circular-dependency metrics plus test-coverage-adjacent signals already in the graph (test node counts per service).

```json
{
  "data": {
    "overallScore": 78,
    "band": "Good",
    "factors": [
      { "name": "Coupling", "score": 72, "weight": 0.4 },
      { "name": "CircularDependencies", "score": 90, "weight": 0.3 },
      { "name": "TestCoverageProxy", "score": 74, "weight": 0.3 }
    ]
  }
}
```

Marked as **proposed/open** — scoring methodology is an open question (§10), not specified by the README beyond naming the goal.

---

## 5. Real-time Updates Design

### 5.1 Hub

`ArchitectureHub : Hub` mounted at `/hubs/architecture` (mapped via `app.MapHub<ArchitectureHub>("/hubs/architecture")`), introduced in Phase 3 alongside the Incremental Watcher.

### 5.2 Trigger flow

1. `arch watch` (Incremental Watcher, `03-incremental-watcher.md`) detects a file change, rebuilds affected nodes, and writes to the Graph Store.
2. The Watcher publishes a domain event (in-process if co-hosted, or via a lightweight message channel/queue if the watcher runs as a separate process — see open question in §10) that the REST API host subscribes to.
3. A `IArchitectureChangeNotifier` service (implemented in the API host) receives the event and calls `IHubContext<ArchitectureHub>.Clients.Group(...).SendAsync(...)`.
4. Connected dashboard clients receive the event and either patch their local TanStack Query cache directly (small diffs) or invalidate + refetch the relevant query key (large diffs, `truncated` cases).

### 5.3 Events

| Event name | Payload | Emitted when |
|---|---|---|
| `scan:progress` | `{ "phase": "Parsing", "filesProcessed": 12, "filesTotal": 40 }` | Watcher is mid-rebuild after detecting changes (useful for a progress indicator) |
| `graph:updated` | `{ "changeId": "chg_881", "addedNodeIds": [...], "removedNodeIds": [...], "updatedNodeIds": [...], "affectedProjectIds": [...] }` | Watcher completes a rebuild cycle and commits new graph state |
| `metrics:updated` | `{ "generatedAtUtc": "...", "totals": { "classCount": 2350, "projectCount": 63 } }` | Emitted alongside `graph:updated` when aggregate metrics changed |
| `job:completed` | `{ "jobId": "job_7f3a", "status": "Completed" }` | An async AI job (§3.6) finishes; dashboard reacts even if the tab that started it isn't the one polling |
| `job:failed` | `{ "jobId": "job_7f3a", "problem": { "title": "...", "status": 503 } }` | Async job fails |

Example `graph:updated` payload:
```json
{
  "changeId": "chg_881",
  "occurredAtUtc": "2026-07-24T10:15:32Z",
  "addedNodeIds": ["class_archive_model"],
  "removedNodeIds": [],
  "updatedNodeIds": ["svc_model_service"],
  "affectedProjectIds": ["proj_business_models"],
  "summary": { "classesAdded": 1, "classesRemoved": 0, "interfacesRemoved": 0 }
}
```

### 5.4 Groups & scoping

* Phase 3 (single-repo, no auth): all clients join an implicit default group on connect; no `JoinRepo` call needed.
* Phase 4 (multi-repo): clients must call a hub method `JoinRepo(repoId)` after connecting; the hub validates the caller's authorization (via the connection's authenticated `ClaimsPrincipal`) against repo membership before adding them to `Clients.Group($"repo:{repoId}")`. All watcher-triggered events are then published only to that repo's group.

### 5.5 Payload size discipline

`graph:updated` intentionally carries node/edge **ids**, not full node payloads, to keep SignalR messages small on large rebuilds. Clients that need full details refetch via `GET /graph` or `GET /services/{id}` using the ids in the event — this also naturally re-validates against the latest server-side pagination/truncation rules.

### 5.6 Transport & reconnection

* SignalR negotiates WebSockets first, falling back to Server-Sent Events / long polling (default SignalR behavior) — relevant for constrained network environments in Phase 4 cloud deployments behind proxies.
* Client uses SignalR's built-in automatic reconnect (`withAutomaticReconnect()`); on reconnect, the dashboard performs a full `GET /graph` + `GET /metrics` refetch to reconcile any events missed while disconnected (no server-side event replay/backlog is planned initially — see open question in §10).

---

## 6. Authentication & Multi-tenancy Design (Phase 4)

### 6.1 Authentication

* **Better Auth** is the identity/session layer (per README's Authentication stack), fronting **GitHub OAuth** and **Microsoft Entra ID** as providers.
* Better Auth issues a session; the ASP.NET Core API validates it via a custom `AuthenticationHandler` (or, if Better Auth exposes a JWT/JWKS-compatible token, standard `AddJwtBearer` against Better Auth's JWKS endpoint — preferred for simplicity if supported, since it avoids a bespoke handler).
* Local dev (Phases 1-3) remains unauthenticated by design (README: "local dev tool"); Phase 4 introduces an `Authentication:Enabled` feature flag so the same codebase can still run auth-free for local/offline use if desired.
* SignalR hub connections authenticate via the same cookie/bearer token passed on the initial handshake (`accessTokenFactory` on the client); `[Authorize]` applied to `ArchitectureHub`.

### 6.2 Multi-repository support

* Every graph-bearing endpoint is scoped under `/api/v1/repos/{repoId}/...`. `repoId` resolution happens in an endpoint filter (`RepoScopeFilter`) that:
  1. Extracts `repoId` from the route.
  2. Resolves the corresponding Graph Store connection/partition (per `02-graph-store.md`'s Phase 4 multi-repo storage model — assumed to key data by `repoId`, whether via separate SQLite files, a `repoId` column in PostgreSQL, or separate Neo4j databases).
  3. Injects a scoped `IGraphReader` (or a `repoId`-bound wrapper) into the request pipeline via `HttpContext.Items` / a scoped DI service, so downstream endpoint handlers never manually thread `repoId` through query calls.

### 6.3 Authorization model

* Roles per repository: `Owner`, `Maintainer`, `Viewer` (team collaboration goal). Stored in a `RepoMembership` table (API-owned, not Graph Store) keyed by `(userId, repoId, role)`.
* Policy-based authorization:
  * `RequireRepoViewer` — all GET endpoints.
  * `RequireRepoMaintainer` — `POST /diagram`, `POST /implementation-plan`, `POST /architecture-analysis` (AI operations cost money/compute; gated above pure viewer).
  * `RequireRepoOwner` — membership management endpoints (`POST /repos/{repoId}/members`, role changes).
* Implemented as an `IAuthorizationHandler` that checks `RepoMembership` for the authenticated user against the route's `repoId`, registered as ASP.NET Core authorization policies (`AddAuthorization(options => options.AddPolicy(...))`).

### 6.4 Shared graphs / team collaboration

* A repo can be shared by inviting a user's email (`POST /repos/{repoId}/invitations`), consistent with "team collaboration (shared graphs, permissions)" from the roadmap.
* No cross-repo graph merging is in scope — "shared" means shared *access* to one repo's graph, not federation across repos (federation is called out as an open question in §10).

### 6.5 Rate limiting & abuse protection

* Phase 4 introduces ASP.NET Core's built-in rate limiting middleware, primarily on the AI-triggering POST endpoints (token/cost-sensitive) and on unauthenticated-adjacent surfaces (none should remain unauthenticated in Phase 4, but the login/OAuth callback endpoints need protection regardless).

---

## 7. Project/Module Structure

Following the existing solution's modular pattern (mirroring the `PatternVision.Modules.*` layering seen in the reference codebase: `Presentation` / `Application` / `Infrastructure` per module):

```
src/
  API/
    ArchIntel.Api/                          # Host project: Program.cs, DI wiring, middleware
      Program.cs
      appsettings.json
      Extensions/
        ServiceCollectionExtensions.cs
        EndpointRouteBuilderExtensions.cs
      Middleware/
        ProblemDetailsConfiguration.cs
        RepoScopeFilter.cs                  # Phase 4
        CorrelationIdMiddleware.cs

  Modules/
    Projects/
      ArchIntel.Modules.Projects.Presentation/
        ProjectsEndpoints.cs                # MapProjectsEndpoints()
        Contracts/ProjectDto.cs
      ArchIntel.Modules.Projects.Application/
        GetProjects/GetProjectsQuery.cs     # thin query object calling IGraphReader

    Services/
      ArchIntel.Modules.Services.Presentation/
        ServicesEndpoints.cs
        Contracts/ServiceDto.cs, ServiceDetailDto.cs
      ArchIntel.Modules.Services.Application/
        GetServices/, GetServiceDetail/

    Graph/
      ArchIntel.Modules.Graph.Presentation/
        GraphEndpoints.cs
        Contracts/GraphDto.cs, SubgraphRequestDto.cs
      ArchIntel.Modules.Graph.Application/
        GetSubgraph/

    Impact/
      ArchIntel.Modules.Impact.Presentation/ImpactEndpoints.cs
      ArchIntel.Modules.Impact.Application/GetImpact/, GetTransitiveImpact/

    Metrics/
      ArchIntel.Modules.Metrics.Presentation/MetricsEndpoints.cs
      ArchIntel.Modules.Metrics.Application/GetMetrics/, GetCouplingMetrics/, GetCircularDependencies/

    Diagram/
      ArchIntel.Modules.Diagram.Presentation/DiagramEndpoints.cs
      ArchIntel.Modules.Diagram.Application/MermaidDiagramRenderer.cs

    Planning/                                # Shared with MCP Server, see 04-mcp-server.md
      ArchIntel.Modules.Planning.Presentation/PlanningEndpoints.cs   # /implementation-plan, /architecture-analysis, /jobs/{id}
      ArchIntel.Modules.Planning.Application/
        GeneratePlan/, AnalyzeArchitecture/
        Jobs/JobStore.cs, Jobs/JobStatus.cs
      ArchIntel.Modules.Planning.Infrastructure/
        LlmPlanningClient.cs                # OpenAI Responses API client (per README's AI Integration stack)

    Realtime/                                # Phase 3+
      ArchIntel.Modules.Realtime.Presentation/
        ArchitectureHub.cs
        IArchitectureChangeNotifier.cs / ArchitectureChangeNotifier.cs

    Snapshots/                               # Phase 4
      ArchIntel.Modules.Snapshots.Presentation/SnapshotsEndpoints.cs
      ArchIntel.Modules.Snapshots.Application/GetSnapshots/, DiffSnapshots/

    Auth/                                    # Phase 4
      ArchIntel.Modules.Auth.Infrastructure/
        BetterAuthAuthenticationHandler.cs (or JwtBearer config against Better Auth JWKS)
      ArchIntel.Modules.Auth.Application/
        RepoMembership/, Invitations/

  Shared/
    ArchIntel.Shared.Contracts/              # ApiEnvelope<T>, PageInfo, NodeRef, ProblemDetails helpers
    ArchIntel.Shared.GraphStoreClient/        # Thin wrapper over the Graph Store reader contract (02-graph-store.md)

tests/
  ArchIntel.Api.IntegrationTests/
  ArchIntel.Modules.Planning.Tests/
  ArchIntel.Modules.Graph.Tests/
```

Design notes:

* Each module's `Presentation` project depends only on its own `Application` project and `Shared.Contracts` — no module references another module's `Presentation`, keeping endpoint groups independently testable/mergeable.
* `Planning` is deliberately structured so its `Application` + `Infrastructure` layers can be extracted into a shared NuGet-style project referenced by both `ArchIntel.Api` and the MCP Server host (per `04-mcp-server.md`) without duplicating logic — this is the "shared Planning Service" mentioned throughout.
* `Shared.GraphStoreClient` is the only place that talks to the Graph Store's reader contract; every module's `Application` layer depends on this abstraction, never on Graph Store internals directly — isolates the API from Graph Store storage-engine changes (SQLite → Postgres/Neo4j).

---

## 8. Deployment Strategy

The README lists four deployment targets for the API: **Azure App Service, Docker, Railway, Fly.io**. A single container image is the common denominator that satisfies all four (App Service can run containers directly; Railway and Fly.io are container-native).

### 8.1 Dockerfile (multi-stage)

```dockerfile
# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "src/API/ArchIntel.Api/ArchIntel.Api.csproj"
RUN dotnet publish "src/API/ArchIntel.Api/ArchIntel.Api.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
COPY --from=build /app/publish .

# SQLite database file lives on a mounted volume in single-instance deployments;
# Phase 4 with PostgreSQL removes this requirement entirely.
VOLUME ["/app/data"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s \
  CMD wget --spider -q http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "ArchIntel.Api.dll"]
```

### 8.2 Target-specific notes

* **Azure App Service (Linux, container-based)**: deploy via `az webapp create --deployment-container-image-name`, configure app settings (`ConnectionStrings__GraphStore`, Better Auth secrets, OpenAI key) through App Service configuration (or Azure Key Vault references) rather than baking them into the image. Use App Service's built-in health check path (`/health`) integration. SignalR requires **"ARR affinity" enabled** (default on App Service) or, for multi-instance scale-out, the **Azure SignalR Service** managed backplane (`AddSignalR().AddAzureSignalR(connectionString)`) — flagged explicitly since App Service's default in-process SignalR does not scale out across instances without a backplane.
* **Docker (generic/self-hosted)**: `docker run -p 8080:8080 -v archintel-data:/app/data --env-file .env archintel-api:latest`. Suitable for a single-VM or docker-compose deployment alongside the dashboard.
* **Railway**: connects directly to the Dockerfile via Railway's Docker build detection; environment variables set through Railway's dashboard/CLI (`railway variables set`); Railway's built-in HTTPS/proxy termination handles WebSocket upgrade for SignalR out of the box.
* **Fly.io**: `fly launch` generates a `fly.toml` from the Dockerfile; set `[[services]] internal_port = 8080` and ensure `auto_stop_machines`/`auto_start_machines` are considered carefully — SignalR long-lived WebSocket connections interact poorly with aggressive auto-suspend, so Fly.io deployments should either disable auto-stop or accept reconnect churn (documented as an operational tradeoff, not solved here).

### 8.3 Configuration & secrets

* All environment-specific values (Graph Store connection string, OpenAI API key for the Planning Service, Better Auth secrets, GitHub/Entra OAuth client id/secret, CORS allowed origins) come from environment variables / platform secret stores — never committed. `appsettings.json` holds only non-secret defaults; `appsettings.Development.json` is gitignored beyond a checked-in `appsettings.Development.json.example`.
* A `/health` endpoint (via `AddHealthChecks()`) checks: Graph Store connectivity, and (Phase 3+) Planning Service reachability (a lightweight "is the LLM provider configured/reachable" probe, not a full round-trip on every health check).

### 8.4 Scaling considerations

* Phase 1-3: single instance is sufficient (local dev tool, then small-team dashboard backing).
* Phase 4 multi-tenant deployment: horizontal scale-out requires (a) the SignalR backplane noted above, and (b) the Graph Store backend to support concurrent multi-instance access (motivates the planned SQLite → PostgreSQL migration called out in the README's Storage roadmap — SQLite's single-writer model is a known constraint for multi-instance API deployments, flagged as a dependency on `02-graph-store.md`'s Phase 4 plan, not solved here).

---

## 9. Testing Strategy

### 9.1 Integration tests via `WebApplicationFactory`

* `ArchIntel.Api.IntegrationTests` uses `WebApplicationFactory<Program>` to spin up the full Minimal API pipeline in-memory, with the Graph Store reader swapped for a seeded in-memory/test SQLite database (a fixed fixture graph representing a small known solution, checked into the test project).
* Coverage per endpoint group: happy path, pagination boundaries (empty page, last page, `hasNextPage` correctness), 404s for unknown ids, validation errors (400) for malformed query params/bodies.
* SignalR: use `Microsoft.AspNetCore.SignalR.Client` against the `WebApplicationFactory`'s `TestServer` to open a real hub connection in-process, trigger a fake watcher event through the injected `IArchitectureChangeNotifier`, and assert the client receives the expected `graph:updated` payload — this is the primary regression guard for §5.
* Async job pattern (§3.6): tests use a fake/deterministic `IPlanningService` (not a real LLM call) so `POST /implementation-plan` tests are fast and non-flaky; a small number of tests are tagged `Manual`/`Integration-LLM` for optional real-LLM smoke testing, excluded from CI by default.

### 9.2 Contract tests against the Graph Store

* Since the API depends on the Graph Store's reader contract (`02-graph-store.md`) rather than its implementation, a dedicated `ArchIntel.Shared.GraphStoreClient.ContractTests` project asserts that:
  * Every method the API needs (`GetProjectsAsync`, `GetServicesAsync`, `GetServiceDetailAsync`, `GetSubgraphAsync`, `GetImpactAsync`/`GetTransitiveImpactAsync`, `GetMetricsAsync`/`GetCouplingMetricsAsync`, snapshot queries in Phase 4) exists with the expected signature and pagination/cursor semantics.
  * These run against whatever the Graph Store module publishes as its own test double/in-memory implementation, so a breaking change to the Graph Store contract fails **this** project's build before it ever reaches a runtime integration issue in the API.
* This is treated as the seam-owning test suite: if `02-graph-store.md`'s contract changes shape, this project is the single place that needs updating on the API side, rather than every endpoint's integration test.

### 9.3 Planning Service contract tests

* Analogous contract tests against the shared Planning Service's `GeneratePlanAsync`/`AnalyzeAsync` signatures, shared conceptually with `04-mcp-server.md`'s own contract tests — both transports assert against the same interface so it can't silently diverge between REST and MCP.

### 9.4 Non-functional / other

* Load/perf smoke test (Phase 3+) for `GET /graph` at realistic scale (thousands of nodes) to validate the depth/scope bounding in §3.5 actually keeps response times and payload sizes reasonable — informal target: sub-500ms for a `depth=2` scoped query on a ~2,500-class graph, revisited once real data exists.
* Authorization tests (Phase 4): matrix of role × endpoint to confirm `RequireRepoViewer`/`Maintainer`/`Owner` policies are applied correctly and that cross-repo access is denied (a Viewer of repo A cannot read repo B's `/graph`).

---

## 10. Risks & Open Questions

* **Watcher-to-API event transport**: this document assumes the Incremental Watcher and REST API can communicate change events cheaply (in-process if co-hosted as one process, or via some IPC/queue if not). `03-incremental-watcher.md` needs to confirm whether the watcher runs *inside* the API host as a `BackgroundService` (simplest — direct in-process event bus) or as a fully separate `arch watch` CLI process (requires a real transport: named pipe, local socket, or lightweight message broker). This materially affects §5.2 and is the single biggest open dependency for Phase 3.
* **SignalR event replay/backlog**: currently no server-side buffering of missed events for reconnecting clients (§5.6) — a client offline for an extended watcher run could miss several `graph:updated` events. Mitigated by full refetch on reconnect, but this could be revisited with a persisted "changes since sequence N" endpoint if reconnect-refetch proves too expensive at scale.
* **Async job durability**: Phase 3's in-memory job store (§3.6) loses in-flight jobs on API restart/redeploy. Acceptable for a local/small-team tool; Phase 4 should back this with a durable table (SQLite/Postgres) if plan generation becomes a heavier, longer-running workload or if multi-instance deployment (§8.4) makes in-memory state actively incorrect (a job started on instance A isn't visible to a poll hitting instance B).
* **SQLite concurrency under Phase 4 multi-instance deployment**: flagged in §8.4 — the REST API's scaling plan is contingent on the Graph Store's own migration to PostgreSQL for multi-writer scenarios; this document cannot resolve it unilaterally.
* **Diagram export formats**: README only specifies Mermaid. Whether PlantUML/SVG/PNG export is ever needed is unconfirmed — `format` field in `POST /diagram` is left extensible but only `mermaid` is planned for implementation now.
* **Architecture quality scoring methodology** (§4.12): the README names this as a Phase 4 goal without specifying inputs/weights. The proposed factor list (coupling, circular dependencies, test-coverage proxy) is this document's best guess, not a confirmed design — needs product input before implementation.
* **Cross-repo federation**: "multi-repository support" is scoped here as *multiple independently-scoped repos under one account*, not a federated graph across repos (e.g., an org-wide dependency graph spanning services in different repos). If that's a real future goal it likely needs a dedicated federation design, not an extension of `RepoScopeFilter`.
* **Better Auth + ASP.NET Core integration specifics**: Better Auth is primarily a TypeScript/Node ecosystem library; whether it can issue a token validated by ASP.NET Core (via JWKS) or whether the dashboard's Next.js layer needs to act as an auth proxy in front of the .NET API is unresolved and should be validated early in Phase 4 planning, since it affects whether §6.1's "standard `AddJwtBearer`" approach is viable or a custom handler is mandatory.
* **Rate limiting thresholds** for AI-triggering endpoints (§6.5) are unspecified — needs real usage data or at least a conservative default before Phase 4 launch.

---

## 11. Task Breakdown

### Phase 1

- [ ] Scaffold `ArchIntel.Api` Minimal API host project, `Program.cs`, DI wiring skeleton
- [ ] Implement `Shared.GraphStoreClient` wrapper over the Graph Store reader contract (SQLite)
- [ ] Implement `GET /projects`
- [ ] Implement `GET /services`
- [ ] Implement `GET /graph` (unfiltered, whole-graph)
- [ ] Global Problem Details exception handling scaffold
- [ ] Basic OpenAPI/Swagger UI for local exploration
- [ ] Bind to `localhost` only, document as a no-auth local dev tool
- [ ] Baseline `WebApplicationFactory` integration test project + first happy-path tests for the three endpoints

### Phase 2

- [ ] Introduce `/api/v1` versioned route prefix; deprecate unprefixed Phase 1 routes
- [ ] Implement cursor pagination (`ApiEnvelope<T>`, `PageInfo`) across list endpoints
- [ ] Implement `GET /services/{id}` (Service Explorer detail)
- [ ] Extend `GET /graph` with `scope`/`depth`/`kinds` filtering and node-count truncation
- [ ] Implement `GET /impact` (direct dependents only)
- [ ] Implement `GET /metrics` (basic totals)
- [ ] Implement `POST /diagram` (Mermaid renderer + endpoint)
- [ ] Configure CORS for dashboard dev/prod origins
- [ ] Validation pipeline (endpoint filter + FluentValidation) for POST bodies and query params
- [ ] Contract tests against Graph Store reader contract (`GraphStoreClient.ContractTests`)
- [ ] Expand integration test coverage (pagination edges, 404s, validation errors)

### Phase 3

- [ ] Stand up `ArchitectureHub` at `/hubs/architecture`; wire `MapHub`
- [ ] Implement `IArchitectureChangeNotifier` and confirm watcher-to-API event transport (resolve open question in §10 first)
- [ ] Emit `scan:progress`, `graph:updated`, `metrics:updated` events
- [ ] Implement async job pattern (`JobStore`, `GET /jobs/{jobId}`, `job:completed`/`job:failed` events)
- [ ] Implement `POST /implementation-plan` calling shared Planning Service `GeneratePlanAsync`
- [ ] Implement `POST /architecture-analysis` calling shared Planning Service `AnalyzeAsync`
- [ ] Extract/confirm shared Planning Service boundary with `04-mcp-server.md` (avoid logic duplication)
- [ ] Upgrade `GET /impact` to transitive impact + risk annotation
- [ ] Add `GET /metrics/coupling` and `GET /metrics/circular-dependencies`
- [ ] SignalR integration tests (fake watcher event -> assert client receives event)
- [ ] Planning Service contract tests (shared with MCP Server's suite where feasible)
- [ ] Load/perf smoke test for `GET /graph` at realistic node counts

### Phase 4

- [ ] Integrate Better Auth session/token validation (resolve JWKS-vs-custom-handler open question first)
- [ ] Wire GitHub OAuth and Microsoft Entra ID providers through Better Auth
- [ ] Introduce `/api/v1/repos/{repoId}/...` scoping and `RepoScopeFilter`
- [ ] Implement `RepoMembership` model and `RequireRepoViewer`/`Maintainer`/`Owner` authorization policies
- [ ] Implement repo invitation endpoints (`POST /repos/{repoId}/invitations`, accept flow)
- [ ] Implement `GET /snapshots` and `GET /snapshots/{id}/diff` (Architecture Timeline)
- [ ] Implement `GET /quality-score` (pending methodology sign-off, §10)
- [ ] Add SignalR group scoping per repo (`JoinRepo`/`LeaveRepo`, authorization check)
- [ ] Add Azure SignalR Service backplane support for multi-instance scale-out
- [ ] Write and validate Dockerfile; test deploys to Azure App Service, Railway, and Fly.io
- [ ] Add `/health` endpoint with Graph Store + Planning Service checks
- [ ] Add rate limiting on AI-triggering endpoints and auth callback endpoints
- [ ] Authorization test matrix (role x endpoint x repo) covering cross-repo denial
- [ ] Document required environment variables/secrets per deployment target
