# 04 — MCP Server Implementation Plan

Component: **MCP Server**
Repo: Architecture Intelligence Platform
Depends on: `02-graph-store.md` (`IGraphReader` contract), `01-architecture-scanner.md` (populates the graph the reader queries)
Consumed by: Claude Code, Codex CLI, Cursor, VS Code, future AI IDEs, and indirectly the Next.js Dashboard (via the shared Application layer, not via MCP itself)

---

## 1. Overview & Responsibilities

The MCP Server is the platform's primary interface for AI coding agents. Its reason for existing is stated directly in the README's core thesis:

> "The AI no longer searches repositories. Instead it requests structured information."

Concretely, the MCP Server is a **read-mostly façade** over the Architecture Graph Store, plus (from Phase 3 onward) a thin orchestration layer that combines graph context with an LLM call to produce implementation plans and architecture analyses. It is deliberately *not* where architectural understanding is computed — that happens in the Scanner and Graph Store. The MCP Server's job is to:

1. **Expose graph queries as typed, schema-validated tools** that an LLM-based agent can call instead of grepping the filesystem or asking the human to paste code.
2. **Bound and shape** graph query results so they are agent-consumable (small, structured, paginated) rather than dumping entire subgraphs.
3. **Own zero business logic of its own.** Every tool handler is a thin adapter that (a) validates input, (b) calls into `IGraphReader` or an `Application` service, (c) maps the result into the tool's output schema. This keeps the MCP Server, the REST API, and the CLI in perfect behavioral sync because they all ultimately call the same Application services.
4. **From Phase 3, orchestrate AI-assisted operations** (`implementation_plan`, `impact_analysis`) that combine a bounded graph subgraph with an LLM call, using a RAG-like pattern instead of asking the LLM to imagine the codebase.
5. **From Phase 4, become multi-repository and identity-aware**, adding repository scoping and a notion of "who is asking" so the platform can start supporting team/cloud scenarios.
6. **Never be the source of truth for anything.** It holds no persistent state of its own beyond transient session/request-scoped context; all durable state lives in the Graph Store.

Non-goals for the MCP Server specifically (handled elsewhere):
- Running the scan / building the graph (Scanner's job).
- Serving the Next.js dashboard (REST API's job — though it shares the same Application services).
- Storing embeddings or doing semantic/documentation search (README explicitly scopes pgvector/embeddings to documentation search, not architectural reasoning — the MCP Server stays graph-first).

---

## 2. Phase-by-Phase Scope

| Phase | Scope for the MCP Server | Depends on |
|---|---|---|
| **Phase 1** | Stand up MCP server scaffolding (stdio transport, tool registration, DI wiring to `IGraphReader`). Implement `find_dependencies`, `find_callers`, `find_service` as simple, direct graph queries. No LLM involved. Ship as a `.NET global tool` / npm-wrapped binary that Claude Code / Cursor / VS Code can launch as a local child process. | Graph Store Phase 1 (SQLite, `IGraphReader` read-only contract), Scanner Phase 1 |
| **Phase 2** | Add `generate_diagram` (reuses the same Mermaid renderer as `arch diagram` CLI and `POST /diagram` REST endpoint — one renderer, three callers). Add richer read tools needed to mirror what the dashboard now offers: `list_projects`, `get_project_overview`, `search_symbols`. Introduce optional HTTP+SSE/Streamable-HTTP transport (still single-user, still local) so the server can be reused by browser-based tooling and to de-risk the transport story ahead of Phase 4. | Graph Store Phase 2 (project/service metadata enrichment), Dashboard Phase 2 (shared diagram renderer) |
| **Phase 3** | Add `impact_analysis` and `implementation_plan` — the first AI-assisted tools. Introduce the RAG-like planning pipeline (subgraph extraction → prompt construction → OpenAI Responses API call → schema-validated structured output). These share an `Application.Planning`/`Application.Analysis` service layer with the REST API's `POST /implementation-plan` and `POST /architecture-analysis` endpoints, so the MCP tool and the dashboard's "AI Planner" feature are two clients of the same brain. | Graph Store Phase 3 (coupling/metrics, circular dependency detection feeding risk scoring), Incremental Watcher (fresher graph = better plans) |
| **Phase 4** | Make tools multi-repository aware (`repositoryId` parameter, `list_repositories` tool), expose architecture quality scoring as a tool (`get_quality_score`), and add team-collaboration context — the server now knows *which user/session* is calling (via hosted auth), enabling audit trails, per-team repository scoping, and usage quotas. Hosted deployment (not just local stdio child process) becomes a first-class mode. | Graph Store Phase 4 (multi-repo model, historical snapshots), Auth (Better Auth / GitHub OAuth / Entra ID) |

---

## 3. MCP Protocol & Technical Design

### 3.1 SDK and hosting model

The MCP Server is a .NET project built on the official **Model Context Protocol C# SDK** (`ModelContextProtocol` NuGet package), consistent with the rest of the backend stack (ASP.NET Core Minimal APIs, C#, .NET). This keeps the tool handlers, DI container, and configuration model identical to the REST API and CLI projects, and lets all three share the same `Application` layer assemblies without any cross-language boundary.

Two hosting shapes exist, selected at startup by configuration, both built from the same tool classes:

**Local / stdio host** (`ArchitectureIntelligence.McpServer.Console`, ships as the `.NET global tool` / npm-wrapped binary referenced in the README's deployment section):

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddArchitectureGraphReader(builder.Configuration)   // registers IGraphReader against local SQLite
    .AddArchitectureApplicationServices();                // IDiagramRenderingService, IArchitectureAnalysisService, IImplementationPlanService

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(FindDependenciesTool).Assembly);

await builder.Build().RunAsync();
```

This is what Claude Code / Cursor / VS Code launch as a child process, registered in the client's own MCP config (e.g. `.mcp.json`, `claude_desktop_config.json`, Cursor's `mcp.json`):

```json
{
  "mcpServers": {
    "architecture-intelligence": {
      "command": "arch-mcp",
      "args": ["--solution", "PatternVision.sln"]
    }
  }
}
```

**Hosted / HTTP host** (`ArchitectureIntelligence.McpServer.Http`, introduced Phase 2 for local multi-client reuse, becomes production-grade Phase 4):

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddArchitectureGraphReader(builder.Configuration)
    .AddArchitectureApplicationServices();

builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        // Stateless in Phase 2 (no per-session state needed yet).
        // Flips to stateful in Phase 4 once repository/user context
        // must be pinned to a session across multiple tool calls.
        options.Stateless = true;
    })
    .WithToolsFromAssembly(typeof(FindDependenciesTool).Assembly);

var app = builder.Build();
app.MapMcp();          // exposes the Streamable HTTP / SSE MCP endpoint
app.Run();
```

Both hosts reference the exact same tool assembly — a tool is written once, and works identically whether reached over stdio or HTTP. This is the same "one implementation, many callers" principle applied to transports as is applied to the diagram renderer and the Application services.

### 3.2 Tool registration

Tools are plain C# classes annotated with SDK attributes; the SDK reflects over them at startup (`WithToolsFromAssembly()`) and auto-generates each tool's JSON Schema from the method signature and `[Description]` metadata:

```csharp
[McpServerToolType]
public sealed class DependencyTools(IGraphReader graphReader)
{
    [McpServerTool(Name = "find_dependencies"),
     Description("Returns the direct or transitive dependencies of a class, interface, service, or project node in the architecture graph.")]
    public async Task<FindDependenciesResult> FindDependencies(
        [Description("Fully-qualified or simple name of the symbol to look up, e.g. 'OrderService' or 'PatternVision.Modules.Orders.OrderService'.")]
        string symbolName,
        [Description("How many relationship hops to traverse. 1 = direct dependencies only. Default 1, max 5.")]
        int depth = 1,
        [Description("Optional filter restricting which relationship kinds to follow, e.g. ['References','Injects']. Omit for all kinds.")]
        string[]? relationshipKinds = null,
        CancellationToken cancellationToken = default)
    {
        // validation, then a direct pass-through to IGraphReader.GetDependencies(...)
    }
}
```

Tools are organized into one class per functional area (`DependencyTools`, `DiagramTools`, `AnalysisTools`, `PlanningTools`, `QualityTools`, `MultiRepoTools`), each taking its Application-layer dependency via constructor injection. This keeps the catalog navigable as it grows from 3 tools (Phase 1) to 13+ (Phase 4) and lets contract tests target one class at a time.

Even though the SDK derives JSON Schema automatically, the project **checks in the expected schema for every tool as a versioned fixture** (`/schemas/tools/*.json`, see §8) so that:
- A change to a method signature that silently changes the public contract fails a test instead of shipping unnoticed.
- Downstream MCP clients (which cache tool schemas) get a deliberate, documented breaking-change process rather than surprise drift.

### 3.3 Transport choice: stdio vs HTTP/SSE

| Concern | stdio | HTTP (Streamable HTTP / SSE) |
|---|---|---|
| Typical caller | Claude Code, Cursor, VS Code launching a local child process | Hosted/team scenarios, browser-adjacent tools, multiple concurrent agent sessions against one running server |
| Trust boundary | Process boundary on the developer's own machine — whoever can launch the process can call any tool | Network boundary — must be authenticated |
| State | Ephemeral per-process; process exits with the editor | Long-running server process; may need per-session state |
| Introduced | Phase 1 (only transport) | Phase 2 (optional, local), Phase 4 (production, hosted) |
| Failure mode | Editor restarts the child process | Requires health checks, reconnect/backoff on the client side |

Phase 1 and most of Phase 2 usage is stdio-only: the MCP Server is launched per-editor-session against a local SQLite graph, matching the README's "Local Scanner ... distributed as npm package / .NET global tool" deployment story. HTTP/SSE is added in Phase 2 as an optional mode (useful once the dashboard exists locally and the team wants to point multiple tools at one already-running server instead of spawning N processes), and becomes the default *hosted* mode in Phase 4 when cloud sync and team collaboration require a server that isn't tied to one developer's machine.

### 3.4 Authentication & session concerns

**Local / stdio (Phases 1–2):** No authentication. The trust boundary is the OS process itself — if you can spawn the MCP server, you already have local filesystem access to the same repository it reads. The one thing the server *does* still validate is that any path-like input (e.g. a project or file filter) resolves inside the configured `scanRoot` / solution directory, to avoid a compromised or overly-creative agent using tool parameters to read arbitrary paths outside the intended repo (defense in depth, not a real authn boundary).

**Local HTTP (Phase 2, optional):** Bound to `localhost` only by default; an optional shared-secret bearer token (config-generated on first run) prevents other local processes/browser tabs on the same machine from calling it unintentionally. Still single-tenant, still assumes one trusted developer.

**Hosted (Phase 4):** This is where real authn/authz shows up, layered on top of the platform's broader auth roadmap (Better Auth, GitHub OAuth, Microsoft Entra ID):
- Every HTTP tool call carries a bearer token; the MCP host resolves it to a `(userId, teamId, allowedRepositoryIds[])` principal before dispatching to any tool.
- MCP sessions become **stateful** (`options.Stateless = false`) so a session pins a principal and (optionally) a "current repository" context negotiated once instead of on every call — this is the technical hook for the README's Phase 4 "team collaboration context (which user/session is asking)."
- Every tool handler receives an `IMcpRequestContext` (wrapping the resolved principal) alongside its typed arguments; `MultiRepoTools` and `PlanningTools` use it to scope graph queries to repositories the caller is authorized for, and `implementation_plan` tags its output with the requesting user for audit/timeline correlation.
- Rate limiting and per-team usage quotas are enforced at this layer, primarily to bound LLM spend from `implementation_plan` (see §9).
- Tool-call audit logging (who asked what, when, against which repo) is written to the same store backing the Architecture Timeline, so "AI asked for an impact analysis of ModelVersion" can eventually show up next to human-made architecture changes.

---

## 4. Tool Catalog

Conventions used below:
- All tools return a `graphVersion` / `lastScannedAt` field so the calling agent can judge data freshness (see §9).
- All list-shaped results are paginated (`pageSize` default 50, `continuationToken`) and carry `truncated: boolean` to make output-size bounding explicit rather than silently dropping data.
- Every tool documents the exact `IGraphReader` (or Application service) method it wraps, so it is traceable to `02-graph-store.md`.

### Phase 1

#### `find_dependencies`

Direct or transitive outbound dependencies of a symbol.

- **Maps to:** `IGraphReader.GetDependencies(nodeId, depth, relationshipKinds)`

Input schema:
```json
{
  "type": "object",
  "properties": {
    "symbolName": { "type": "string", "description": "Simple or fully-qualified symbol name, e.g. 'OrderService'." },
    "depth": { "type": "integer", "minimum": 1, "maximum": 5, "default": 1 },
    "relationshipKinds": {
      "type": "array",
      "items": { "type": "string", "enum": ["References", "Calls", "Implements", "Inherits", "Injects", "Uses", "Publishes", "Consumes", "Owns", "Contains"] }
    }
  },
  "required": ["symbolName"]
}
```

Output schema:
```json
{
  "type": "object",
  "properties": {
    "rootNode": { "$ref": "#/definitions/GraphNode" },
    "dependencies": { "type": "array", "items": { "$ref": "#/definitions/GraphEdgeResult" } },
    "truncated": { "type": "boolean" },
    "graphVersion": { "type": "string" },
    "lastScannedAt": { "type": "string", "format": "date-time" }
  }
}
```

Example request:
```json
{ "tool": "find_dependencies", "arguments": { "symbolName": "OrderService", "depth": 2 } }
```

Example response:
```json
{
  "rootNode": { "id": "n_orderservice", "name": "OrderService", "kind": "Service", "project": "PatternVision.Modules.Orders.Application" },
  "dependencies": [
    { "relationship": "Injects", "depth": 1, "node": { "id": "n_iorderrepo", "name": "IOrderRepository", "kind": "Interface", "project": "PatternVision.Modules.Orders.Domain" } },
    { "relationship": "Uses", "depth": 1, "node": { "id": "n_dbcontext", "name": "OrdersDbContext", "kind": "EfCoreDbContext", "project": "PatternVision.Modules.Orders.Infrastructure" } },
    { "relationship": "References", "depth": 2, "node": { "id": "n_sqlserver", "name": "SQL Server", "kind": "ExternalSystem", "project": null } }
  ],
  "truncated": false,
  "graphVersion": "2026-07-24T02:11:00Z#4821",
  "lastScannedAt": "2026-07-24T02:11:00Z"
}
```

#### `find_callers`

Inbound edges — who depends on / calls this symbol. The reverse of `find_dependencies`.

- **Maps to:** `IGraphReader.GetCallers(nodeId, depth)`

Input schema:
```json
{
  "type": "object",
  "properties": {
    "symbolName": { "type": "string" },
    "depth": { "type": "integer", "minimum": 1, "maximum": 5, "default": 1 }
  },
  "required": ["symbolName"]
}
```

Output schema: identical shape to `find_dependencies`, with `dependencies` renamed to `callers` and relationship direction reversed.

Example request/response:
```json
{ "tool": "find_callers", "arguments": { "symbolName": "IOrderRepository" } }
```
```json
{
  "rootNode": { "id": "n_iorderrepo", "name": "IOrderRepository", "kind": "Interface", "project": "PatternVision.Modules.Orders.Domain" },
  "callers": [
    { "relationship": "Implements", "depth": 1, "node": { "id": "n_orderrepo", "name": "OrderRepository", "kind": "Class", "project": "PatternVision.Modules.Orders.Infrastructure" } },
    { "relationship": "Injects", "depth": 1, "node": { "id": "n_orderservice", "name": "OrderService", "kind": "Service", "project": "PatternVision.Modules.Orders.Application" } }
  ],
  "truncated": false,
  "graphVersion": "2026-07-24T02:11:00Z#4821",
  "lastScannedAt": "2026-07-24T02:11:00Z"
}
```

#### `find_service`

Resolves a fuzzy/partial name to one or more graph nodes classified as services (or the closest matching kind), used as the entry point before calling more specific tools.

- **Maps to:** `IGraphReader.FindByName(query, kindFilter: ["Service","Controller","HostedService","MinimalApiEndpoint"])`

Input schema:
```json
{
  "type": "object",
  "properties": {
    "query": { "type": "string", "description": "Partial or full name, case-insensitive." },
    "kinds": { "type": "array", "items": { "type": "string" }, "description": "Optional narrower kind filter." },
    "maxResults": { "type": "integer", "default": 10, "maximum": 50 }
  },
  "required": ["query"]
}
```

Output schema:
```json
{
  "type": "object",
  "properties": {
    "matches": { "type": "array", "items": { "$ref": "#/definitions/GraphNode" } },
    "truncated": { "type": "boolean" }
  }
}
```

Example:
```json
{ "tool": "find_service", "arguments": { "query": "order" } }
```
```json
{
  "matches": [
    { "id": "n_orderservice", "name": "OrderService", "kind": "Service", "project": "PatternVision.Modules.Orders.Application" },
    { "id": "n_ordercontroller", "name": "OrdersController", "kind": "Controller", "project": "PatternVision.Modules.Orders.Presentation" }
  ],
  "truncated": false
}
```

### Phase 2

#### `generate_diagram`

Renders a Mermaid diagram for a project, service, or arbitrary subgraph. Backed by the **same renderer** used by `arch diagram` (CLI) and `POST /diagram` (REST) — implemented once in `ArchitectureIntelligence.Application.Diagrams`.

- **Maps to:** `IDiagramRenderingService.RenderMermaid(scope, diagramType, depth)`, which internally calls `IGraphReader.GetSubgraph(...)`

Input schema:
```json
{
  "type": "object",
  "properties": {
    "scope": { "type": "string", "description": "Project name, service name, or 'solution' for the whole graph." },
    "diagramType": { "type": "string", "enum": ["dependencyGraph", "sequenceOfCalls", "componentOverview"], "default": "dependencyGraph" },
    "depth": { "type": "integer", "minimum": 1, "maximum": 4, "default": 2 },
    "maxNodes": { "type": "integer", "default": 75, "maximum": 300 }
  },
  "required": ["scope"]
}
```

Output schema:
```json
{
  "type": "object",
  "properties": {
    "mermaidSource": { "type": "string" },
    "nodeCount": { "type": "integer" },
    "truncated": { "type": "boolean" }
  }
}
```

Example:
```json
{ "tool": "generate_diagram", "arguments": { "scope": "PatternVision.Modules.Orders", "diagramType": "dependencyGraph" } }
```
```json
{
  "mermaidSource": "graph TD\n  OrdersController-->IOrderService\n  IOrderService-->OrderService\n  OrderService-->IOrderRepository\n  IOrderRepository-->OrderRepository\n  OrderRepository-->SQLServer",
  "nodeCount": 5,
  "truncated": false
}
```

#### `list_projects`

- **Maps to:** `IGraphReader.ListProjects(filter?)`

Input schema:
```json
{ "type": "object", "properties": { "nameContains": { "type": "string" } } }
```

Output schema:
```json
{ "type": "object", "properties": { "projects": { "type": "array", "items": { "$ref": "#/definitions/ProjectSummary" } } } }
```

#### `get_project_overview`

- **Maps to:** `IGraphReader.GetProjectOverview(projectId)` — counts of classes/interfaces/services, direct project references, test coverage presence.

Input schema:
```json
{ "type": "object", "properties": { "projectName": { "type": "string" } }, "required": ["projectName"] }
```

Output schema:
```json
{
  "type": "object",
  "properties": {
    "project": { "$ref": "#/definitions/ProjectSummary" },
    "classCount": { "type": "integer" },
    "interfaceCount": { "type": "integer" },
    "serviceCount": { "type": "integer" },
    "referencedProjects": { "type": "array", "items": { "type": "string" } },
    "referencingProjects": { "type": "array", "items": { "type": "string" } },
    "hasAssociatedTestProject": { "type": "boolean" }
  }
}
```

#### `search_symbols`

General-purpose symbol search across all node kinds (broader than `find_service`), the MCP equivalent of the dashboard's search box.

- **Maps to:** `IGraphReader.SearchSymbols(query, kindFilter?, projectFilter?)`

Input schema:
```json
{
  "type": "object",
  "properties": {
    "query": { "type": "string" },
    "kinds": { "type": "array", "items": { "type": "string" } },
    "projectNameContains": { "type": "string" },
    "maxResults": { "type": "integer", "default": 20, "maximum": 100 }
  },
  "required": ["query"]
}
```

### Phase 3

#### `impact_analysis`

Given a symbol, returns everything affected by changing it, classified by component type — this is the MCP equivalent of the dashboard's Impact Analysis view.

- **Maps to:** `IArchitectureAnalysisService.AnalyzeImpact(nodeId, depth)`, which composes `IGraphReader.GetImpact(nodeId, depth)` with classification rules (Controller/API, Repository, Validator, Test, BackgroundWorker, etc.)

Input schema:
```json
{
  "type": "object",
  "properties": {
    "symbolName": { "type": "string" },
    "depth": { "type": "integer", "minimum": 1, "maximum": 6, "default": 3 }
  },
  "required": ["symbolName"]
}
```

Output schema:
```json
{
  "type": "object",
  "properties": {
    "rootNode": { "$ref": "#/definitions/GraphNode" },
    "affected": {
      "type": "object",
      "properties": {
        "api": { "type": "array", "items": { "$ref": "#/definitions/GraphNode" } },
        "repositories": { "type": "array", "items": { "$ref": "#/definitions/GraphNode" } },
        "validators": { "type": "array", "items": { "$ref": "#/definitions/GraphNode" } },
        "tests": { "type": "array", "items": { "$ref": "#/definitions/GraphNode" } },
        "backgroundWorkers": { "type": "array", "items": { "$ref": "#/definitions/GraphNode" } },
        "other": { "type": "array", "items": { "$ref": "#/definitions/GraphNode" } }
      }
    },
    "totalAffectedCount": { "type": "integer" },
    "truncated": { "type": "boolean" }
  }
}
```

Example (mirrors the README's `ModelVersion` example):
```json
{ "tool": "impact_analysis", "arguments": { "symbolName": "ModelVersion" } }
```
```json
{
  "rootNode": { "id": "n_modelversion", "name": "ModelVersion", "kind": "Entity", "project": "PatternVision.Modules.Models.Domain" },
  "affected": {
    "api": [{ "id": "n_modelscontroller", "name": "ModelsController", "kind": "Controller", "project": "PatternVision.Modules.Models.Presentation" }],
    "repositories": [{ "id": "n_modelrepo", "name": "ModelVersionRepository", "kind": "Class", "project": "PatternVision.Modules.Models.Infrastructure" }],
    "validators": [{ "id": "n_modelvalidator", "name": "ModelVersionValidator", "kind": "Class", "project": "PatternVision.Modules.Models.Application" }],
    "tests": [{ "id": "n_modeltests", "name": "ModelVersionTests", "kind": "TestClass", "project": "PatternVision.Modules.Models.Tests" }],
    "backgroundWorkers": [{ "id": "n_modelsync", "name": "ModelSyncWorker", "kind": "HostedService", "project": "PatternVision.Modules.Models.Infrastructure" }],
    "other": []
  },
  "totalAffectedCount": 5,
  "truncated": false
}
```

#### `implementation_plan`

The AI-assisted planner — full design in §5.

- **Maps to:** `IImplementationPlanService.GeneratePlan(request)` (this itself calls `IGraphReader` for context gathering, then an LLM client)

Input schema:
```json
{
  "type": "object",
  "properties": {
    "requestText": { "type": "string", "description": "Natural-language feature/change request, e.g. 'Implement Archive Model'." },
    "anchorSymbols": { "type": "array", "items": { "type": "string" }, "description": "Optional explicit symbols to seed context gathering; if omitted, the planner infers anchors from requestText." },
    "maxContextDepth": { "type": "integer", "default": 3, "maximum": 5 }
  },
  "required": ["requestText"]
}
```

Output schema: see §5.4 (`ImplementationPlanResult`).

### Phase 4

#### `list_repositories`

- **Maps to:** `IGraphReader.ListRepositories(callerPrincipal)` — scoped to repositories the caller's token grants access to.

```json
{ "type": "object", "properties": { "matches": { "type": "array", "items": { "$ref": "#/definitions/RepositorySummary" } } } }
```

#### `get_quality_score`

Exposes the architecture quality scoring computed for the Coupling Heatmap / metrics feature as a callable tool.

- **Maps to:** `IArchitectureAnalysisService.ComputeQualityScore(repositoryId, scope?)`

Input schema:
```json
{
  "type": "object",
  "properties": {
    "repositoryId": { "type": "string" },
    "scope": { "type": "string", "description": "Optional project/module name to scope the score; omit for whole-repo score." }
  },
  "required": ["repositoryId"]
}
```

Output schema:
```json
{
  "type": "object",
  "properties": {
    "overallScore": { "type": "number", "minimum": 0, "maximum": 100 },
    "couplingScore": { "type": "number" },
    "circularDependencyCount": { "type": "integer" },
    "hotspots": { "type": "array", "items": { "type": "object", "properties": { "project": { "type": "string" }, "coupling": { "type": "string", "enum": ["green", "yellow", "red"] } } } }
  }
}
```

#### `find_dependencies` / `find_callers` / `impact_analysis` (extended)

All Phase 1–3 tools gain an optional `repositoryId` parameter in Phase 4 so a caller with access to multiple repositories can disambiguate; omitting it defaults to the single repository configured for the current session (backward compatible with Phases 1–3 usage).

#### `whoami`

Lightweight session-introspection tool giving the calling agent (and, transitively, the developer) visibility into the identity/scope it is operating under — the technical surface for "team collaboration context."

- **Maps to:** `IMcpRequestContext` resolved by the hosted server's auth middleware (§3.4), not a graph query.

Output schema:
```json
{
  "type": "object",
  "properties": {
    "userId": { "type": "string" },
    "teamId": { "type": "string" },
    "allowedRepositoryIds": { "type": "array", "items": { "type": "string" } },
    "sessionId": { "type": "string" }
  }
}
```

---

## 5. AI Implementation Planner Design (Phase 3)

### 5.1 Why RAG-over-the-graph instead of RAG-over-embeddings

The platform's stated principle is "AI should reason using architecture, not raw source code." The planner therefore does not embed and retrieve source snippets; it retrieves **structured graph context** (nodes, relationships, kinds, project boundaries) and lets the LLM reason over that. This is smaller, more deterministic, and directly traceable to graph node IDs, which makes hallucination detectable (§5.5) in a way that free-text code retrieval is not.

### 5.2 Pipeline

```
requestText
    │
    ▼
1. Anchor resolution ── FindByName / SearchSymbols over requestText keywords
    │                    (falls back to a cheap LLM entity-extraction call only if no direct match)
    ▼
2. Subgraph gathering ── GetDependencies + GetCallers + GetImpact per anchor, bounded by maxContextDepth
    │                    plus project metadata, existing test coverage, DI registration info
    ▼
3. Context serialization ── compact tabular/textual form (node id, kind, project, relationship), NOT source code
    │
    ▼
4. Prompt construction ── system prompt (rules + output schema) + user content (request + serialized subgraph
    │                      + relevant config: scanOrder, naming conventions)
    ▼
5. LLM call ── OpenAI Responses API, structured output constrained to ImplementationPlanResult JSON Schema
    │
    ▼
6. Schema validation ── reject/retry once on malformed JSON
    │
    ▼
7. Grounding check ── every referenced project/service/file cross-checked against the gathered subgraph;
    │                  unrecognized names flagged as "unverified" and riskLevel raised
    ▼
8. Persist + return ── plan stored (audit/timeline correlation), returned to caller (MCP tool or REST endpoint)
```

### 5.3 Prompt construction strategy

- **System prompt** establishes: (a) role ("senior architect assistant reasoning strictly from the provided graph context"), (b) hard rule: do not invent projects/services not present in the supplied context unless explicitly proposing them as `newFiles`, (c) the exact output JSON Schema (Responses API `text.format = json_schema`, `strict: true`), (d) guidance on effort/risk estimation heuristics (e.g., "touching more than 5 projects or any project with `circularDependencyCount > 0` is at least Medium risk").
- **User content**: original `requestText`, the serialized subgraph (grouped by project, capped at a configurable node budget — default 150 nodes / ~6k tokens — with `truncated: true` surfaced back to the caller if exceeded), and repository conventions pulled from the scanner config (`scanOrder`, module layering rules) so the plan respects the project's own architectural conventions (e.g., Domain must not depend on Infrastructure).
- **Few-shot examples** (optional, added once real plans accumulate): 1–2 prior accepted plans for similar request shapes, retrieved by simple keyword similarity — not embeddings, to avoid introducing a second retrieval system.
- **Model tiering for cost control**: anchor/entity extraction (step 1 fallback) uses a small/cheap model; the actual plan synthesis (step 5) uses the stronger model, since that is the output developers act on.

### 5.4 Structured output schema

Matches the README's AI Planner feature list exactly (`Affected projects / New files / Modified services / Database changes / Tests required / Risk level / Estimated effort`), plus fields needed for traceability and grounding:

```json
{
  "type": "object",
  "properties": {
    "requestText": { "type": "string" },
    "summary": { "type": "string", "description": "One-paragraph plain-English restatement of the plan." },
    "affectedProjects": { "type": "array", "items": { "type": "string" } },
    "newFiles": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "path": { "type": "string" },
          "project": { "type": "string" },
          "purpose": { "type": "string" }
        },
        "required": ["path", "project", "purpose"]
      }
    },
    "modifiedServices": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "nodeId": { "type": ["string", "null"], "description": "Graph node id if grounded; null if proposed/new." },
          "name": { "type": "string" },
          "changeDescription": { "type": "string" }
        },
        "required": ["name", "changeDescription"]
      }
    },
    "databaseChanges": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "entity": { "type": "string" },
          "changeType": { "type": "string", "enum": ["newTable", "newColumn", "migration", "index", "other"] },
          "description": { "type": "string" }
        }
      }
    },
    "testsRequired": { "type": "array", "items": { "type": "string" } },
    "riskLevel": { "type": "string", "enum": ["Low", "Medium", "High"] },
    "riskFactors": { "type": "array", "items": { "type": "string" } },
    "estimatedEffort": {
      "type": "object",
      "properties": {
        "size": { "type": "string", "enum": ["XS", "S", "M", "L", "XL"] },
        "hoursLow": { "type": "number" },
        "hoursHigh": { "type": "number" }
      }
    },
    "groundedNodeIds": { "type": "array", "items": { "type": "string" }, "description": "Node ids from the supplied subgraph that this plan actually referenced — used by the grounding check." },
    "unverifiedReferences": { "type": "array", "items": { "type": "string" }, "description": "Names the model mentioned that could not be matched to a graph node." }
  },
  "required": ["requestText", "summary", "affectedProjects", "modifiedServices", "testsRequired", "riskLevel", "estimatedEffort"]
}
```

Example response (for `requestText: "Implement Archive Model"`):
```json
{
  "requestText": "Implement Archive Model",
  "summary": "Add an archival workflow for ModelVersion: a new ArchiveModelCommand/Handler, a soft-delete flag on the entity, and API + background worker updates.",
  "affectedProjects": [
    "PatternVision.Modules.Models.Domain",
    "PatternVision.Modules.Models.Application",
    "PatternVision.Modules.Models.Infrastructure",
    "PatternVision.Modules.Models.Presentation",
    "PatternVision.Modules.Models.Tests"
  ],
  "newFiles": [
    { "path": "Application/Models/ArchiveModel/ArchiveModelCommand.cs", "project": "PatternVision.Modules.Models.Application", "purpose": "MediatR command for archiving a model version." },
    { "path": "Application/Models/ArchiveModel/ArchiveModelCommandHandler.cs", "project": "PatternVision.Modules.Models.Application", "purpose": "Handler applying the archive state transition." }
  ],
  "modifiedServices": [
    { "nodeId": "n_modelversion", "name": "ModelVersion", "changeDescription": "Add IsArchived + ArchivedAtUtc properties." },
    { "nodeId": "n_modelscontroller", "name": "ModelsController", "changeDescription": "Add POST /models/{id}/archive endpoint." },
    { "nodeId": "n_modelsync", "name": "ModelSyncWorker", "changeDescription": "Skip archived models in sync loop." }
  ],
  "databaseChanges": [
    { "entity": "ModelVersion", "changeType": "newColumn", "description": "Add IsArchived (bit) and ArchivedAtUtc (datetime2, nullable)." },
    { "entity": "ModelVersion", "changeType": "migration", "description": "EF Core migration AddModelVersionArchiveFields." }
  ],
  "testsRequired": [
    "ArchiveModelCommandHandlerTests",
    "ModelsController archive endpoint integration test",
    "ModelSyncWorker skips archived models test"
  ],
  "riskLevel": "Medium",
  "riskFactors": ["Touches a background worker with existing scheduled-job side effects.", "Database migration on a high-traffic table."],
  "estimatedEffort": { "size": "M", "hoursLow": 6, "hoursHigh": 12 },
  "groundedNodeIds": ["n_modelversion", "n_modelscontroller", "n_modelsync"],
  "unverifiedReferences": []
}
```

### 5.5 Grounding / anti-hallucination check

After schema validation, a deterministic post-processing pass:
1. Collects every `nodeId` referenced in `modifiedServices[].nodeId`.
2. Confirms each exists in the subgraph gathered in step 2 (§5.2) — not just "exists somewhere in the whole graph," but was actually part of what was handed to the model, closing the loop on whether the model used the given context or invented an answer.
3. Any `modifiedServices` entry with `nodeId: null` that isn't clearly framed as new/proposed, or any project name in `affectedProjects` not present in `groundedNodeIds`' owning projects, is moved into `unverifiedReferences` and the plan's `riskLevel` is raised by one notch with a `riskFactors` entry noting "contains ungrounded references — verify manually before implementing."

This check is intentionally conservative and mechanical (string/ID matching against the known subgraph), not another LLM call — determinism here matters more than sophistication, and it is exactly what the golden-output tests in §8 pin down.

---

## 6. Integration with REST API and Dashboard

The MCP Server and the REST API are two transports over one brain. Neither owns planning or analysis logic:

```
                         ArchitectureIntelligence.Application
                    (IGraphQueryService, IDiagramRenderingService,
                     IArchitectureAnalysisService, IImplementationPlanService)
                                        │
                ┌───────────────────────┼────────────────────────┐
                │                       │                        │
     ArchitectureIntelligence.McpServer │           ArchitectureIntelligence.Api
     (find_dependencies, find_callers,  │           (GET /projects, /services, /graph,
      find_service, generate_diagram,   │            /impact, /metrics; POST /implementation-plan,
      impact_analysis,                  │            /diagram, /architecture-analysis)
      implementation_plan, ...)         │
                                        │
                              ArchitectureIntelligence.Cli
                          (arch explain, arch impact, arch diagram)
```

Concretely:
- `POST /implementation-plan` (REST) and the `implementation_plan` MCP tool both call `IImplementationPlanService.GeneratePlan(request)`. The dashboard's **AI Planner** feature (developer types "Implement Archive Model" into the UI) is a thin React form that posts to `POST /implementation-plan` — it produces byte-for-byte the same `ImplementationPlanResult` shape an AI agent gets back over MCP. This is a deliberate design choice: a plan generated by Claude Code via MCP and a plan generated by a human via the dashboard are interchangeable and comparable, and both can be persisted to the same plan history/audit table.
- `POST /architecture-analysis` accepts a `mode` discriminator (`impact | coupling | quality`) and dispatches to `IArchitectureAnalysisService`; the MCP `impact_analysis` tool always calls it with `mode: impact`, and the Phase 4 `get_quality_score` tool always calls it with `mode: quality`. The dashboard's Impact Analysis view and Coupling Heatmap call the same endpoint with the other modes. One service, three UIs (MCP tool, REST consumer, CLI).
- `generate_diagram` (MCP), `POST /diagram` (REST, feeds the dashboard's graph views where a static Mermaid export is wanted, e.g. exporting to docs), and `arch diagram` (CLI) all call `IDiagramRenderingService.RenderMermaid(...)`.
- Read-only tools (`find_dependencies`, `find_callers`, `find_service`, `list_projects`, `search_symbols`) call `IGraphQueryService`, which is a near 1:1 pass-through to `IGraphReader` — this layer exists purely so the MCP tool handlers and the REST `GET` endpoints don't each hand-roll their own pagination/truncation logic.

This layering means **the MCP Server implementation plan and the REST API implementation plan should be read together**: the MCP Server ships tool wrappers; the actual behavior lives in `ArchitectureIntelligence.Application`, documented in more depth wherever the REST API's own implementation plan covers those endpoints.

---

## 7. Project/Module Structure

```
ArchitectureIntelligence.sln
│
├── src/
│   ├── ArchitectureIntelligence.GraphStore.Abstractions/      # IGraphReader, GraphNode, GraphEdgeResult, etc. (owned by 02-graph-store.md)
│   │
│   ├── ArchitectureIntelligence.Application/
│   │   ├── Queries/           # IGraphQueryService (thin pass-through for read tools)
│   │   ├── Diagrams/          # IDiagramRenderingService, Mermaid renderer
│   │   ├── Analysis/          # IArchitectureAnalysisService (impact, coupling, quality)
│   │   └── Planning/          # IImplementationPlanService, prompt builder, LLM client wrapper, grounding checker
│   │
│   ├── ArchitectureIntelligence.McpServer/                    # shared tool classes + DTOs, referenced by both hosts
│   │   ├── Tools/
│   │   │   ├── DependencyTools.cs        (find_dependencies, find_callers)
│   │   │   ├── DiscoveryTools.cs         (find_service, list_projects, get_project_overview, search_symbols)
│   │   │   ├── DiagramTools.cs           (generate_diagram)
│   │   │   ├── AnalysisTools.cs          (impact_analysis)
│   │   │   ├── PlanningTools.cs          (implementation_plan)
│   │   │   ├── MultiRepoTools.cs         (list_repositories, get_quality_score, whoami)
│   │   ├── Contracts/                    # request/response DTOs + JSON Schema fixtures
│   │   └── Mapping/                      # GraphNode -> DTO mappers shared across tools
│   │
│   ├── ArchitectureIntelligence.McpServer.Console/            # stdio host, packaged as .NET global tool / npm wrapper
│   ├── ArchitectureIntelligence.McpServer.Http/                # HTTP/SSE host, ASP.NET Core, adds auth middleware (Phase 4)
│   │
│   ├── ArchitectureIntelligence.Api/                          # REST API (separate implementation plan doc)
│   └── ArchitectureIntelligence.Cli/                          # CLI (separate implementation plan doc)
│
└── tests/
    ├── ArchitectureIntelligence.McpServer.Tests/               # tool contract tests, schema fixture tests
    ├── ArchitectureIntelligence.Application.Planning.Tests/    # golden-output planner tests, mocked LLM client
    └── ArchitectureIntelligence.McpServer.IntegrationTests/    # end-to-end stdio client against seeded SQLite fixture DB
```

Key dependency rule: `McpServer` depends on `Application` and `GraphStore.Abstractions` only — never directly on a specific `IGraphReader` implementation (SQLite/Postgres/Neo4j), which stays swappable per `02-graph-store.md`.

---

## 8. Testing Strategy

### 8.1 Tool contract tests

- For every tool, an in-process MCP client (SDK provides an in-memory/test transport) issues `tools/list` and asserts the returned JSON Schema matches the checked-in fixture in `Contracts/schemas/*.json` — catches accidental breaking changes to a tool's public contract.
- For every tool, a `tools/call` test with valid input asserts the response against the output schema (structural validation, not exact values) using a seeded fixture graph (`FixtureGraphReader`, an in-memory `IGraphReader` fake with a small deterministic graph: `OrderController -> IOrderService -> OrderService -> IOrderRepository -> OrderRepository -> SqlServer`, mirroring the README's own Graph Store example).
- Negative-path tests: unknown `symbolName` returns a structured "not found" result (empty `matches`/`dependencies` + explanatory message), never an unhandled exception surfaced as a raw MCP error — agents need to be able to recover from "no match" gracefully.
- Input validation tests: missing required field, `depth` out of range, invalid `relationshipKinds` enum value — all should produce a clear MCP tool-input-validation error, not a 500-equivalent.

### 8.2 Golden-output tests for the planner

- A fixed, larger fixture graph (`OrdersModuleFixture`, ~40 nodes across Domain/Application/Infrastructure/Presentation/Tests projects) is checked into the test project so subgraph gathering (§5.2 step 2) is fully deterministic.
- The LLM call itself is replaced by a `FakeResponsesClient` returning a canned, schema-valid JSON payload keyed by the input prompt hash — no real OpenAI API calls in CI, no flakiness, no cost.
- Golden files (`*.golden.json`) capture the full `ImplementationPlanResult` for a fixed set of `requestText` inputs (e.g., "Implement Archive Model", "Add pagination to OrderService", "Remove IOrderRepository interface"); tests do a structural diff against the golden file and fail loudly (with a readable diff) on any change to prompt construction, subgraph gathering, or grounding-check logic that alters the final shape.
- A **separate, manually-triggered** test suite (excluded from normal CI via a trait/category, run on demand or nightly) exercises the real OpenAI Responses API against the same fixture graph, asserting only *structural* schema validity and grounding-check pass rate — not exact content — since real LLM output is not expected to be byte-stable. This is the safety net for "did we break the actual integration," separate from "did we break our own logic."

### 8.3 Schema/version regression tests

- Each tool's SDK-derived JSON Schema is snapshot-tested against `Contracts/schemas/<toolName>.json` on every build; intentional changes require updating the fixture in the same PR, forcing a conscious review of contract changes.

### 8.4 Integration tests

- Spin up `ArchitectureIntelligence.McpServer.Console` as a real child process over stdio, backed by a seeded temporary SQLite database, and drive it with the SDK's MCP client — verifies `initialize` → `tools/list` → `tools/call` end-to-end, catching wiring bugs invisible to in-process tests (DI registration order, serialization edge cases, process startup).
- Phase 4 adds an HTTP-hosted integration test asserting: unauthenticated calls are rejected, a token scoped to `repositoryId: A` cannot retrieve nodes from `repositoryId: B`, and `whoami` reflects the token's claims correctly.

### 8.5 Performance/size-bounding tests

- A synthetic large fixture graph (~5,000 nodes) exercises `find_dependencies`/`impact_analysis` with `depth: 5` to confirm truncation kicks in (`truncated: true`, bounded node count) rather than the tool call ballooning into a multi-megabyte response.

---

## 9. Risks & Open Questions

| Risk / Question | Notes / Mitigation |
|---|---|
| **LLM hallucination in `implementation_plan`** | Mitigated by the grounding check (§5.5), strict JSON-schema-constrained output, and treating the plan as a draft for human review — never auto-applied. Open question: should low-confidence plans (high `unverifiedReferences` count) be rejected outright rather than returned with a warning? |
| **LLM cost control** | Model tiering (cheap model for entity extraction, stronger model for synthesis), per-team usage quotas in hosted mode (Phase 4), and caching identical `(requestText, graphVersion)` pairs to avoid re-paying for repeat questions against an unchanged graph. Needs a concrete per-team monthly budget defined before Phase 4 hosted rollout. |
| **Tool output size limits** | Every list-shaped tool is paginated and depth/node-capped (§4 conventions); large graphs must degrade to `truncated: true` rather than blowing past MCP message size limits or the calling agent's context window. Open question: should truncation be node-count-based, byte-size-based, or token-estimate-based — likely needs to be configurable per deployment (different AI IDEs have different context budgets). |
| **Determinism of AI-assisted tools** | Non-AI tools are fully deterministic and cheap to golden-test. AI tools are not — golden tests pin our *own* logic (prompt construction, grounding check) with a mocked LLM client; real-API structural tests are a separate, lower-frequency suite (§8.2). |
| **Data freshness** | Between scans/watcher cycles the graph can be stale relative to uncommitted local edits. Every response carries `lastScannedAt`; whether tools should proactively warn ("this graph is 3 days old") or trigger an on-demand incremental rescan before answering is an open design question tied to the Incremental Watcher's own implementation plan. |
| **Path/security validation in local mode** | Stdio mode has no real authn boundary (§3.4); tool inputs that resemble file paths must still be validated against the configured `scanRoot` allowlist to avoid an agent using a tool argument to probe outside the intended repository. |
| **Auth model evolution** | Phase 1–2 intentionally ship with no/minimal auth to avoid over-engineering a single-user local tool. The `IMcpRequestContext` abstraction is introduced early (even if trivially populated in local mode) specifically so Phase 4's real auth doesn't require a tool-handler rewrite — open question is exactly when (Phase 2 vs 3) to introduce the abstraction even if unused, to avoid a disruptive Phase 4 refactor. |
| **MCP protocol/SDK churn** | The C# SDK is still evolving (transport APIs, stateful/stateless session semantics). Pin an explicit SDK version per release and track breaking changes in a changelog; do not chase pre-release SDK versions in production hosts. |
| **Multi-repo cross-repository edges (Phase 4)** | `find_dependencies`/`impact_analysis` assume same-repo edges. Whether the Graph Store will model cross-repo relationships (e.g., a NuGet package published by Repo A consumed by Repo B) at all is an open question owned by `02-graph-store.md` Phase 4 — the MCP tool contracts above assume it will, but the tools degrade gracefully (repo-scoped results only) if it doesn't land in time. |
| **Tool schema backward compatibility** | Once AI IDEs cache a tool's schema, changing required fields or enum values is a breaking change from the caller's perspective. Needs an explicit versioning/deprecation policy (e.g., additive-only changes within a major version, new tool name for breaking shape changes) before Phase 2 ships enough tools for this to matter. |
| **Rate limiting / abuse (hosted mode)** | Needs a concrete policy (requests/minute per user, concurrent `implementation_plan` calls per team) before Phase 4 hosted deployment — currently unscoped. |

---

## 10. Task Breakdown

### Phase 1 — Basic MCP server

- [ ] Scaffold `ArchitectureIntelligence.McpServer` (shared tool library) and `ArchitectureIntelligence.McpServer.Console` (stdio host) projects.
- [ ] Add `ModelContextProtocol` SDK dependency; wire `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`.
- [ ] Define `GraphNode`, `GraphEdgeResult`, `ProjectSummary` shared DTOs in `Contracts/`.
- [ ] Implement `IGraphQueryService` as a pass-through wrapper over `IGraphReader` with pagination/truncation helpers.
- [ ] Implement `find_dependencies`, `find_callers`, `find_service` tool classes + input validation.
- [ ] Check in JSON Schema fixtures for all three tools; add schema snapshot tests.
- [ ] Build `FixtureGraphReader` test double and the small deterministic fixture graph.
- [ ] Write contract tests (positive + negative paths) for all three tools.
- [ ] Write stdio child-process integration test against a seeded SQLite file.
- [ ] Package as `.NET global tool`; verify launch via a minimal `.mcp.json`/Claude Code config.
- [ ] Document local setup (how a developer points an AI IDE at the server).

### Phase 2 — Diagram export & richer read tools

- [ ] Extract/confirm `IDiagramRenderingService` (Mermaid renderer) as a shared Application-layer component reusable by CLI, REST, and MCP.
- [ ] Implement `generate_diagram` tool with `diagramType`/`depth`/`maxNodes` truncation.
- [ ] Implement `list_projects`, `get_project_overview`, `search_symbols` tools.
- [ ] Add `ArchitectureIntelligence.McpServer.Http` host (`WithHttpTransport(Stateless = true)`, `app.MapMcp()`), localhost-only by default with optional shared-secret bearer token.
- [ ] Add schema fixtures + contract tests for the four new tools.
- [ ] Add performance/size-bounding test with a synthetic large fixture graph.
- [ ] Update docs: stdio vs local-HTTP usage guidance.

### Phase 3 — AI-assisted tools

- [ ] Design and implement `Application.Planning` (`IImplementationPlanService`, subgraph gatherer, prompt builder, LLM client wrapper for OpenAI Responses API, grounding checker).
- [ ] Design and implement `Application.Analysis.AnalyzeImpact` (classification rules for API/Repository/Validator/Test/BackgroundWorker/Other).
- [ ] Implement `impact_analysis` MCP tool.
- [ ] Implement `implementation_plan` MCP tool.
- [ ] Define and lock the `ImplementationPlanResult` JSON Schema (§5.4); align with REST `POST /implementation-plan` response shape.
- [ ] Build `OrdersModuleFixture` (~40-node fixture graph) and `FakeResponsesClient`.
- [ ] Write golden-output tests for at least 3 representative `requestText` scenarios.
- [ ] Add manually-triggered "real LLM, structural-only" test suite, excluded from default CI.
- [ ] Implement model tiering (cheap entity-extraction model vs synthesis model) and request-level caching keyed on `(requestText, graphVersion)`.
- [ ] Wire `POST /architecture-analysis` (REST) and the dashboard's AI Planner UI to the same `Application` services (coordinate with REST API implementation plan).
- [ ] Add `lastScannedAt`/`graphVersion` freshness fields across all tool outputs.

### Phase 4 — Multi-repo, quality scoring, team context

- [ ] Introduce `IMcpRequestContext` (principal: `userId`, `teamId`, `allowedRepositoryIds`) threaded through tool handlers.
- [ ] Add auth middleware to `ArchitectureIntelligence.McpServer.Http` (bearer token → resolved principal), integrate with Better Auth/OAuth roadmap.
- [ ] Flip HTTP host to stateful sessions (`Stateless = false`) where a session pins principal + current repository context.
- [ ] Add `repositoryId` parameter to all existing Phase 1–3 tools (backward-compatible default).
- [ ] Implement `list_repositories`, `get_quality_score`, `whoami` tools.
- [ ] Implement per-team rate limiting and LLM usage quotas for `implementation_plan`.
- [ ] Implement tool-call audit logging feeding the Architecture Timeline.
- [ ] Add authorization/repo-scoping integration tests (cross-tenant isolation).
- [ ] Define and document the tool-schema versioning/deprecation policy.
- [ ] Load-test the hosted HTTP server under multi-session concurrent tool calls.
