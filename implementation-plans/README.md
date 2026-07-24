# Implementation Plans — Index

Detailed, per-component implementation plans for the [Architecture Intelligence Platform](../README.md), covering all four roadmap phases. Each document is independently actionable but cross-references the others where components integrate — those integration points are called out explicitly as "assumed contracts" so teams can work in parallel without blocking on each other.

## Documents

| # | Document | Component | Owns |
|---|----------|-----------|------|
| 1 | [01-architecture-scanner.md](01-architecture-scanner.md) | Architecture Scanner | Roslyn/MSBuild-based parsing, symbol resolution, dependency extraction, the `IGraphWriter` output contract |
| 2 | [02-graph-store.md](02-graph-store.md) | Graph Store | Node/edge schema, SQLite → Postgres/Neo4j migration path, `IGraphWriter`/`IGraphReader` contracts |
| 3 | [03-cli.md](03-cli.md) | CLI | `arch` command surface, config scaffolding, distribution as a .NET global tool |
| 4 | [04-mcp-server.md](04-mcp-server.md) | MCP Server | Tool catalog for AI agents, AI implementation planner (RAG-over-graph) |
| 5 | [05-rest-api.md](05-rest-api.md) | REST API | Minimal API endpoints, SignalR live updates, auth surface for the dashboard |
| 6 | [06-dashboard.md](06-dashboard.md) | Next.js Dashboard | The 7 dashboard views, graph rendering strategy, real-time reconciliation |
| 7 | [07-incremental-watcher.md](07-incremental-watcher.md) | Incremental Watcher | `arch watch`, file-change detection, blast-radius rescoping, live notification |
| 8 | [08-cross-cutting-concerns.md](08-cross-cutting-concerns.md) | Cross-Cutting | Monorepo layout, config schema versioning, auth architecture, CI/CD, deployment, observability |

## How the components fit together

```text
                 Git Repository
                        │
                 Architecture Scanner (01)
                        │  IGraphWriter
                        ▼
               Graph Store (02) ── SQLite → Postgres/Neo4j
                        ▲  IGraphReader
        ┌───────────────┼────────────────┐
        │               │                │
   MCP Server (04)  REST API (05)    CLI (03)
        │               │
  Claude/Codex      Next.js Dashboard (06)
                        ▲
                        │ SignalR
              Incremental Watcher (07)

Cross-Cutting Concerns (08): config schema, auth, CI/CD,
deployment, and repo layout underpin all of the above.
```

## Phase → document map

| Phase | Primary focus | Documents most active this phase |
|-------|---------------|-----------------------------------|
| **Phase 1** | Solution scanner, dependency graph, SQLite storage, CLI, basic MCP server | 01, 02, 03, 04 (basic tools), 08 (local-only setup) |
| **Phase 2** | Next.js dashboard, interactive dependency graph, impact analysis, Mermaid export | 02 (query support), 03 (`diagram`), 04 (`generate_diagram`), 05 (full endpoint set), 06 (app shell + 4 core views), 08 (single-instance deploy) |
| **Phase 3** | AI implementation planner, incremental watcher, metrics, coupling, circular dependency detection | 01 (incremental scanning, metrics), 02 (versioning, metrics storage), 03 (`watch`/`metrics`/`impact`/`callers`), 04 (planner, impact analysis), 05 (planner endpoints, SignalR), 06 (Timeline, Heatmap, AI Planner UI), 07 (full watcher) |
| **Phase 4** | Cloud sync, team collaboration, historical snapshots, multi-repo, quality scoring | 01 (multi-language groundwork), 02 (Postgres/Neo4j migration, multi-repo schema), 03 (cloud commands), 04 (multi-repo tools), 05 (auth, multi-tenancy), 06 (auth UI, collaboration), 07 (multi-repo watching, cloud sync), 08 (auth architecture, multi-tenant deployment) |

## Key cross-component contracts

These interfaces are defined once (in their owning document) and consumed by others. When implementing a consuming component, treat the contract as authoritative from the owning document — don't redefine it locally.

- **`IGraphWriter` / node & edge DTOs** — owned by [02-graph-store.md](02-graph-store.md), implemented against by [01-architecture-scanner.md](01-architecture-scanner.md) and [07-incremental-watcher.md](07-incremental-watcher.md).
- **`IGraphReader` / query methods** — owned by [02-graph-store.md](02-graph-store.md), consumed by [03-cli.md](03-cli.md), [04-mcp-server.md](04-mcp-server.md), and [05-rest-api.md](05-rest-api.md).
- **Planning Service** (implementation-plan generation) — shared application-service layer consumed by both [04-mcp-server.md](04-mcp-server.md)'s `implementation_plan` tool and [05-rest-api.md](05-rest-api.md)'s `POST /implementation-plan`.
- **SignalR `ArchitectureHub` events** — owned by [05-rest-api.md](05-rest-api.md), triggered by [07-incremental-watcher.md](07-incremental-watcher.md), consumed by [06-dashboard.md](06-dashboard.md).
- **`arch.config.yaml` schema** — owned by [08-cross-cutting-concerns.md](08-cross-cutting-concerns.md), consumed by [01-architecture-scanner.md](01-architecture-scanner.md) and [03-cli.md](03-cli.md).

## Status

All 8 documents are first-draft implementation plans (~7,000 lines total) generated from the [project README](../README.md). They have **not yet been cross-reviewed for consistency** (e.g., do the DTO field names in 01 exactly match what 02 expects?) — that reconciliation pass is a recommended next step before starting Phase 1 implementation.
