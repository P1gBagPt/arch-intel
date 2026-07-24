# 02 — Graph Store: Implementation Plan

## 0. Document Purpose

This document is the authoritative implementation plan for the **Graph Store** component of the Architecture Intelligence Platform. The Graph Store is the persistence and query layer sitting between the **Architecture Scanner** / **Incremental Watcher** (writers) and the **MCP Server**, **REST API**, **CLI**, and (indirectly) **Next.js Dashboard** (readers).

Because the Scanner team is implementing `01-architecture-scanner.md` in parallel against the contracts defined here, **Section 4 (Writer Contract)** and **Section 5 (Reader/Query Contract)** are considered frozen interfaces once reviewed. Any breaking change to these contracts requires a version bump and cross-team sign-off.

This plan covers all four roadmap phases end-to-end so the schema and interfaces are designed correctly from day one, even though only a subset is built in Phase 1.

---

## 1. Overview & Responsibilities

The Graph Store is **the single source of truth** for the architectural model of a scanned codebase. It is not a generic database wrapper — it is a purpose-built persistence engine for a **typed, directed, attributed property graph** representing software architecture (projects, types, members, infrastructure, and the relationships between them).

### 1.1 What the Graph Store Owns

* The **node table** (architectural entities: projects, namespaces, classes, interfaces, methods, controllers, repositories, message queues, external systems, etc.)
* The **edge table** (typed relationships: `References`, `Calls`, `Implements`, `Inherits`, `Injects`, `Uses`, `Publishes`, `Consumes`, `Owns`, `Contains`)
* **Project/solution metadata** (solution name, project list, scan configuration used)
* **Snapshot / history** records for the Architecture Timeline (Phase 3+)
* **Metrics** (coupling, complexity, fan-in/fan-out, cyclicality) computed from the graph (Phase 3+)
* **Multi-repository partitioning** metadata (Phase 4)
* A thin adjacency to the **embeddings index** (pgvector) for semantic/documentation search — the Graph Store does not compute embeddings, but Phase 4 co-locates the vector table in the same Postgres instance and the Graph Store exposes a pass-through query surface for it (see §7.4).

### 1.2 What the Graph Store Does NOT Own

* Parsing source code (Scanner's job)
* Deciding *when* to rescan a file (Incremental Watcher's job, though the Graph Store must support the delete/upsert primitives that make incremental updates possible)
* Rendering graphs (Dashboard's job — the Graph Store only returns nodes/edges/subgraphs as data)
* Generating implementation plans or calling LLMs (AI Planner's job — it consumes Graph Store queries as context)
* Computing OpenAI embeddings (a separate indexing pipeline; the Graph Store only stores/queries vectors once computed, in Phase 4)

### 1.3 Design Principles

1. **Graph-first, not ORM-first.** Traversal queries (find callers, find dependents, path-finding, cycle detection) are the primary workload. The schema and data-access layer are optimized for adjacency-list style recursive queries, not for object-graph hydration.
2. **Contract stability over implementation convenience.** The Writer and Reader contracts are C# interfaces with plain DTOs so the storage engine can change (SQLite → Postgres → Neo4j) without touching consumers.
3. **Idempotent writes.** Every write operation is an upsert keyed by a stable, deterministic node/edge ID so re-scanning or incremental updates never create duplicates.
4. **Everything is timestamped and versioned** from Phase 1 onward (even though the timeline/history *reporting* feature ships in Phase 3), because retrofitting `created_at`/`updated_at`/`scan_version` columns onto an existing dataset is painful.
5. **Storage-agnostic query contract.** No SQL leaks into consumers. The Reader interface returns DTOs, never `DataTable`/`DbConnection`/raw rows.

---

## 2. Phase-by-Phase Scope

| Phase | Graph Store Deliverables |
|---|---|
| **Phase 1** | SQLite storage engine; core schema (nodes, edges, projects, scan_runs); `IGraphWriter` v1 (upsert node/edge, begin/commit scan); `IGraphReader` v1 (get node by id, find by name, get dependencies/callers, list projects); used by Scanner, basic MCP server, CLI (`arch scan`, `arch explain`) |
| **Phase 2** | Subgraph extraction (`GetSubgraph`, `GetNeighborhood`), impact analysis traversal (`GetImpact`), filtering (by project, node type, relationship type, depth), Mermaid/DOT export helpers, pagination for large graphs, indices tuned for interactive graph rendering (Cytoscape/Sigma/React Flow), REST API + Dashboard wired against `IGraphReader` |
| **Phase 3** | Incremental upsert/delete-stale primitives keyed by file path + scan version; `scan_runs` / `snapshots` history tables for the Architecture Timeline; metrics tables (coupling, complexity, fan-in/fan-out); circular dependency detection query; versioned node/edge rows (soft-delete + `valid_from`/`valid_to`); Incremental Watcher wired against `IGraphWriter` v2 |
| **Phase 4** | PostgreSQL backend (EF Core / Dapper dual-provider or Dapper + provider-specific SQL), optional Neo4j adapter for deep traversal workloads, `repo_id` partitioning for multi-repository support, cloud sync (push/pull of snapshots), architecture quality scoring table, pgvector-adjacent embeddings table wiring |

---

## 3. Data Model / Schema Design

### 3.1 Storage & Data-Access Technology Choice

**Decision: Dapper (micro-ORM) + hand-written SQL + a lightweight migration runner (DbUp), not EF Core.**

Justification:

1. **The core workload is graph traversal, not object hydration.** Queries like "find all transitive callers of `IOrderService` up to depth 5" or "detect cycles in the project reference graph" are naturally expressed as **recursive CTEs** (`WITH RECURSIVE` in SQLite/Postgres). EF Core's LINQ provider does not translate recursive traversal well; you end up dropping to raw SQL anyway, which defeats the point of the abstraction.
2. **Portability across SQLite → PostgreSQL → (optionally) Neo4j** is easier when the SQL is explicit and small per-provider, rather than relying on EF Core's provider abstraction to paper over syntax differences that don't actually align for graph queries (e.g., `RETURNING`, `ON CONFLICT`, JSON functions, recursive CTE syntax differences).
3. **Performance and predictability.** Dapper maps rows to DTOs with near-ADO.NET performance and no change-tracking overhead — important because scans and incremental updates can touch tens of thousands of nodes/edges in one run.
4. **Migrations stay explicit.** Using DbUp (or FluentMigrator, TBD in Phase 1 spike) with plain `.sql` scripts keeps the schema history readable and lets the same scripts be adapted (not literally reused) for Postgres in Phase 4, rather than fighting EF Core's provider-specific migration generators.
5. **Trade-off accepted:** we lose EF Core's automatic migration diffing and LINQ convenience. This is acceptable because the Graph Store is a bounded component with a small, deliberately-designed schema (not a general-purpose CRUD app with 100 evolving entities).

Connection management: a single `IDbConnectionFactory` abstraction (`SqliteConnectionFactory` in Phase 1, `NpgsqlConnectionFactory` added in Phase 4) so the rest of the codebase never references `Microsoft.Data.Sqlite` or `Npgsql` directly.

### 3.2 Core Tables (SQLite, Phase 1 baseline — carried through all phases with additive migrations)

```sql
-- ============================================================
-- 001_init.sql (Phase 1)
-- ============================================================

CREATE TABLE projects (
    project_id      TEXT PRIMARY KEY,          -- deterministic hash: sha1(solution + project path)
    repo_id         TEXT NOT NULL DEFAULT 'default', -- multi-repo partitioning, unused until Phase 4
    name            TEXT NOT NULL,
    path            TEXT NOT NULL,              -- relative path to .csproj
    target_framework TEXT,
    project_type    TEXT,                       -- 'ClassLibrary' | 'Web' | 'Test' | 'Worker' | ...
    layer           TEXT,                       -- 'Domain' | 'Application' | 'Infrastructure' | 'API' | 'Tests' (from scanOrder config)
    created_at      TEXT NOT NULL,              -- ISO-8601 UTC
    updated_at      TEXT NOT NULL,
    scan_version    INTEGER NOT NULL DEFAULT 1  -- FK-like ref to scan_runs.id that last touched this row
);

CREATE TABLE nodes (
    node_id         TEXT PRIMARY KEY,           -- deterministic hash: sha1(project_id + namespace + name + kind)
    repo_id         TEXT NOT NULL DEFAULT 'default',
    project_id      TEXT NOT NULL REFERENCES projects(project_id),
    node_type       TEXT NOT NULL,              -- see NodeType enum, §4.1
    name            TEXT NOT NULL,               -- simple name, e.g. "OrderService"
    full_name       TEXT NOT NULL,               -- fully qualified, e.g. "PatternVision.Orders.OrderService"
    namespace       TEXT,
    file_path       TEXT,                        -- relative to repo root; NULL for synthetic/external nodes
    line_start      INTEGER,
    line_end        INTEGER,
    metadata_json    TEXT NOT NULL DEFAULT '{}', -- free-form JSON: modifiers, DI lifetime, HTTP verb, etc.
    is_external      INTEGER NOT NULL DEFAULT 0, -- 1 for nodes representing external systems (SQL Server, Kafka, etc.)
    is_deleted       INTEGER NOT NULL DEFAULT 0, -- soft-delete flag (Phase 3+)
    valid_from       TEXT NOT NULL,              -- versioning (Phase 3+), ISO-8601 UTC
    valid_to         TEXT,                        -- NULL = current
    created_at       TEXT NOT NULL,
    updated_at       TEXT NOT NULL,
    scan_version     INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE edges (
    edge_id          TEXT PRIMARY KEY,           -- deterministic hash: sha1(source_id + target_id + relationship_type)
    repo_id          TEXT NOT NULL DEFAULT 'default',
    source_id        TEXT NOT NULL REFERENCES nodes(node_id),
    target_id        TEXT NOT NULL REFERENCES nodes(node_id),
    relationship_type TEXT NOT NULL,             -- see RelationshipType enum, §4.1
    metadata_json     TEXT NOT NULL DEFAULT '{}', -- e.g. call site line number, injection lifetime
    is_deleted        INTEGER NOT NULL DEFAULT 0,
    valid_from        TEXT NOT NULL,
    valid_to          TEXT,
    created_at        TEXT NOT NULL,
    updated_at        TEXT NOT NULL,
    scan_version      INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE scan_runs (
    scan_run_id      INTEGER PRIMARY KEY AUTOINCREMENT,
    repo_id          TEXT NOT NULL DEFAULT 'default',
    started_at       TEXT NOT NULL,
    completed_at     TEXT,
    scan_type        TEXT NOT NULL,              -- 'Full' | 'Incremental'
    triggered_by     TEXT,                        -- 'cli' | 'watcher' | 'ci'
    changed_files_json TEXT,                       -- for incremental runs: list of file paths that triggered it
    status           TEXT NOT NULL DEFAULT 'Running', -- 'Running' | 'Completed' | 'Failed'
    error_message    TEXT
);

-- Indices (Phase 1)
CREATE INDEX idx_nodes_project        ON nodes(project_id);
CREATE INDEX idx_nodes_type           ON nodes(node_type);
CREATE INDEX idx_nodes_name           ON nodes(name);
CREATE INDEX idx_nodes_full_name      ON nodes(full_name);
CREATE INDEX idx_nodes_file_path      ON nodes(file_path);
CREATE INDEX idx_edges_source         ON edges(source_id);
CREATE INDEX idx_edges_target         ON edges(target_id);
CREATE INDEX idx_edges_relationship   ON edges(relationship_type);
CREATE INDEX idx_edges_source_rel     ON edges(source_id, relationship_type);
CREATE INDEX idx_edges_target_rel     ON edges(target_id, relationship_type);
```

### 3.3 Phase 2 Additions (graph rendering support)

No new tables required — Phase 2 is primarily new *queries* (subgraph extraction, filtering) against the existing schema, plus:

```sql
-- ============================================================
-- 002_phase2_indices.sql
-- ============================================================

-- Composite index to speed up "give me all edges within project X" for subgraph rendering
CREATE INDEX idx_nodes_project_type ON nodes(project_id, node_type);

-- Covering index for neighborhood queries (both directions considered via UNION query, see §5)
CREATE INDEX idx_edges_full ON edges(source_id, target_id, relationship_type, edge_id);
```

### 3.4 Phase 3 Additions (incremental updates, metrics, timeline)

```sql
-- ============================================================
-- 003_phase3_metrics_and_history.sql
-- ============================================================

CREATE TABLE node_metrics (
    node_id          TEXT NOT NULL REFERENCES nodes(node_id),
    scan_run_id      INTEGER NOT NULL REFERENCES scan_runs(scan_run_id),
    fan_in           INTEGER NOT NULL DEFAULT 0,
    fan_out          INTEGER NOT NULL DEFAULT 0,
    coupling_score   REAL,
    complexity_score REAL,               -- e.g. cyclomatic complexity if available from scanner metadata
    computed_at      TEXT NOT NULL,
    PRIMARY KEY (node_id, scan_run_id)
);

CREATE TABLE project_metrics (
    project_id        TEXT NOT NULL REFERENCES projects(project_id),
    scan_run_id        INTEGER NOT NULL REFERENCES scan_runs(scan_run_id),
    afferent_coupling  INTEGER NOT NULL DEFAULT 0,  -- Ca: projects depending on this one
    efferent_coupling  INTEGER NOT NULL DEFAULT 0,  -- Ce: projects this one depends on
    instability        REAL,                          -- Ce / (Ca + Ce)
    node_count          INTEGER NOT NULL DEFAULT 0,
    computed_at         TEXT NOT NULL,
    PRIMARY KEY (project_id, scan_run_id)
);

CREATE TABLE circular_dependencies (
    cycle_id         TEXT PRIMARY KEY,      -- hash of sorted node_id list
    scan_run_id      INTEGER NOT NULL REFERENCES scan_runs(scan_run_id),
    node_ids_json     TEXT NOT NULL,         -- ordered list of node_ids forming the cycle
    cycle_length      INTEGER NOT NULL,
    severity          TEXT,                  -- 'Warning' | 'Error', based on config thresholds
    detected_at       TEXT NOT NULL
);

-- Architecture Timeline: one row per scan_run summarizing deltas vs previous scan_run
CREATE TABLE snapshots (
    snapshot_id       INTEGER PRIMARY KEY AUTOINCREMENT,
    repo_id           TEXT NOT NULL DEFAULT 'default',
    scan_run_id       INTEGER NOT NULL REFERENCES scan_runs(scan_run_id),
    taken_at          TEXT NOT NULL,
    total_projects    INTEGER NOT NULL,
    total_nodes       INTEGER NOT NULL,
    total_edges       INTEGER NOT NULL,
    nodes_added        INTEGER NOT NULL DEFAULT 0,
    nodes_removed      INTEGER NOT NULL DEFAULT 0,
    nodes_modified     INTEGER NOT NULL DEFAULT 0,
    edges_added         INTEGER NOT NULL DEFAULT 0,
    edges_removed       INTEGER NOT NULL DEFAULT 0,
    projects_added       INTEGER NOT NULL DEFAULT 0,
    projects_removed     INTEGER NOT NULL DEFAULT 0,
    summary_json          TEXT NOT NULL DEFAULT '{}' -- structured per-type breakdown, e.g. {"classes": {"added": 28}, "interfaces": {"removed": 1}}
);

CREATE INDEX idx_node_metrics_scan     ON node_metrics(scan_run_id);
CREATE INDEX idx_project_metrics_scan  ON project_metrics(scan_run_id);
CREATE INDEX idx_snapshots_taken_at    ON snapshots(taken_at);
```

Versioning fields already present in `nodes`/`edges` (`valid_from`, `valid_to`, `is_deleted`) are activated in Phase 3: an incremental rescan of a file does not `DELETE` rows outright — it closes the validity window (`valid_to = now`) of stale rows tied to that file and inserts fresh rows with a new `valid_from`. A hard-delete sweep (configurable retention, e.g. 30 days) can be run later to reclaim space. This gives the Timeline feature a true history to diff against without needing a separate audit log.

### 3.5 Phase 4 Additions (multi-repo, quality scoring, cloud sync)

```sql
-- ============================================================
-- 004_phase4_multirepo_and_scoring.sql
-- ============================================================

CREATE TABLE repositories (
    repo_id           TEXT PRIMARY KEY,
    name              TEXT NOT NULL,
    remote_url        TEXT,
    default_branch    TEXT,
    last_synced_at    TEXT,
    created_at        TEXT NOT NULL
);

CREATE TABLE architecture_quality_scores (
    repo_id            TEXT NOT NULL REFERENCES repositories(repo_id),
    scan_run_id        INTEGER NOT NULL REFERENCES scan_runs(scan_run_id),
    overall_score       REAL NOT NULL,           -- 0-100
    coupling_component   REAL,
    cyclicality_component REAL,
    layering_component    REAL,                   -- violations of configured layer rules (e.g. Domain -> Infrastructure)
    test_coverage_component REAL,
    computed_at           TEXT NOT NULL,
    breakdown_json         TEXT NOT NULL DEFAULT '{}',
    PRIMARY KEY (repo_id, scan_run_id)
);

CREATE TABLE sync_log (
    sync_id            INTEGER PRIMARY KEY AUTOINCREMENT,
    repo_id            TEXT NOT NULL REFERENCES repositories(repo_id),
    direction           TEXT NOT NULL,   -- 'Push' | 'Pull'
    remote_endpoint     TEXT NOT NULL,
    started_at          TEXT NOT NULL,
    completed_at         TEXT,
    status               TEXT NOT NULL DEFAULT 'Running',
    bytes_transferred     INTEGER,
    error_message         TEXT
);
```

Note: `repo_id` is added to every table starting Phase 1 (default `'default'`) specifically so Phase 4 multi-repo support is a **data migration** (populate real repo_ids, add FK to `repositories`), not a schema rewrite.

### 3.6 Mapping to PostgreSQL (Phase 4)

| SQLite construct | PostgreSQL equivalent | Notes |
|---|---|---|
| `TEXT PRIMARY KEY` (hash id) | `TEXT PRIMARY KEY` or `UUID` | Keep TEXT hash ids for cross-backend portability of IDs already persisted in SQLite-era data |
| `INTEGER AUTOINCREMENT` | `BIGSERIAL` / `GENERATED ALWAYS AS IDENTITY` | `scan_run_id`, `snapshot_id`, `sync_id` |
| `TEXT` timestamps (ISO-8601) | `TIMESTAMPTZ` | Migration script parses ISO strings into native timestamps |
| `metadata_json TEXT` | `JSONB` | Enables indexed JSON queries (`GIN` index) in Postgres, not available in SQLite |
| `WITH RECURSIVE` CTEs | Same syntax, Postgres supports it natively | Query layer's recursive traversal SQL is 95% portable as-is |
| No array type | `TEXT[]` for `node_ids_json` in `circular_dependencies` (optional) | Can stay JSON for simplicity/portability |
| N/A | `pgvector` extension + `embeddings` table | Added alongside, not replacing, the graph tables (see §7.4) |

Migration path: a one-time `arch migrate --to postgres` command reads all rows via `IGraphReader`-adjacent bulk-export queries and re-inserts via the Postgres `IGraphWriter` implementation, inside a transaction, validating row counts before/after. Both backends implement the **same** `IGraphWriter`/`IGraphReader` contracts, so the migration tool is mostly plumbing, not business logic.

### 3.7 Mapping to Neo4j (Phase 4, optional)

Neo4j is **optional** and positioned as an alternate backend for organizations with very large graphs (100k+ nodes) needing deep multi-hop traversal performance that relational recursive CTEs handle poorly.

* `nodes` row → Neo4j node with label = `node_type` (e.g. `:Class`, `:Interface`, `:Controller`) and properties = all scalar columns + `metadata_json` flattened or stored as a map property.
* `edges` row → Neo4j relationship with type = `relationship_type` (e.g. `:CALLS`, `:IMPLEMENTS`) and properties = `metadata_json`.
* Traversal queries (`GetImpact`, `FindPaths`, cycle detection) are reimplemented in Cypher behind the same `IGraphReader` interface (e.g. `impact_analysis` becomes a variable-length path match `MATCH (n)-[*1..5]->(m) WHERE n.node_id = $id`).
* Because Neo4j is optional, the `IGraphReader`/`IGraphWriter` implementations are behind a `GraphBackend` enum + factory (`GraphStoreFactory.Create(config.Backend)`), so choosing Neo4j is a configuration change, not a code change for consumers.
* Given the added operational complexity (running a Neo4j instance), Neo4j support ships **after** Postgres and is explicitly marked "experimental" until there's a real customer need — this is a hedge, not a committed Phase 4 deliverable.

---

## 4. Writer Contract

This is the contract the **Architecture Scanner** and **Incremental Watcher** implement against. It must remain stable; changes require updating `01-architecture-scanner.md` in lockstep.

### 4.1 Shared Enums & DTOs

```csharp
namespace ArchIntel.GraphStore.Contracts;

public enum NodeType
{
    Solution,
    Project,
    Assembly,
    Namespace,
    Class,
    Interface,
    Record,
    Struct,
    Enum,
    Method,
    Constructor,
    Property,
    Field,
    Controller,
    MinimalApiEndpoint,
    MediatRHandler,
    DomainEvent,
    IntegrationEvent,
    EfEntity,
    EfDbContext,
    Repository,
    Service,
    BackgroundWorker,
    HostedService,
    MessageQueue,
    ExternalSystem,      // e.g. "SQL Server", "Kafka", "Redis" — synthetic nodes
    ConfigurationSection,
    TestClass,
    TestMethod
}

public enum RelationshipType
{
    References,   // project -> project, namespace -> namespace
    Calls,        // method -> method
    Implements,   // class -> interface
    Inherits,     // class -> class
    Injects,      // constructor/service -> interface/class (DI)
    Uses,         // generic usage not covered by a more specific type
    Publishes,    // handler/service -> event
    Consumes,     // handler/service -> event
    Owns,         // aggregate root -> entity, dbcontext -> entity
    Contains      // project -> namespace -> class -> method (structural containment)
}

/// <summary>Node DTO used for both write input and read output.</summary>
public sealed record NodeDto
{
    public required string NodeId { get; init; }      // deterministic id; see IIdGenerator
    public string RepoId { get; init; } = "default";
    public required string ProjectId { get; init; }
    public required NodeType NodeType { get; init; }
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public string? Namespace { get; init; }
    public string? FilePath { get; init; }
    public int? LineStart { get; init; }
    public int? LineEnd { get; init; }
    public bool IsExternal { get; init; } = false;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

/// <summary>Edge DTO used for both write input and read output.</summary>
public sealed record EdgeDto
{
    public required string EdgeId { get; init; }       // deterministic id; see IIdGenerator
    public string RepoId { get; init; } = "default";
    public required string SourceId { get; init; }
    public required string TargetId { get; init; }
    public required RelationshipType RelationshipType { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record ProjectDto
{
    public required string ProjectId { get; init; }
    public string RepoId { get; init; } = "default";
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string? TargetFramework { get; init; }
    public string? ProjectType { get; init; }
    public string? Layer { get; init; }
}

/// <summary>Deterministic ID generation so re-scans are naturally idempotent.</summary>
public interface IIdGenerator
{
    string NodeId(string projectId, string? @namespace, string fullName, NodeType nodeType);
    string EdgeId(string sourceId, string targetId, RelationshipType relationshipType);
    string ProjectId(string solutionPath, string projectPath);
}
```

### 4.2 `IGraphWriter` (Phase 1 baseline)

```csharp
namespace ArchIntel.GraphStore.Contracts;

public interface IGraphWriter
{
    /// <summary>
    /// Starts a scan run. Must be called before any Upsert* calls in a scan.
    /// Returns a scan handle carrying the scan_run_id used to stamp all writes in this run.
    /// </summary>
    Task<ScanHandle> BeginScanAsync(BeginScanRequest request, CancellationToken ct = default);

    Task UpsertProjectAsync(ScanHandle scan, ProjectDto project, CancellationToken ct = default);

    /// <summary>Upsert is keyed by NodeDto.NodeId. Existing row is updated in place; new row is inserted otherwise.</summary>
    Task UpsertNodeAsync(ScanHandle scan, NodeDto node, CancellationToken ct = default);

    /// <summary>Batch variant — required for full scans touching thousands of nodes; implementations MUST batch internally (e.g. multi-row INSERT / transaction).</summary>
    Task UpsertNodesAsync(ScanHandle scan, IReadOnlyCollection<NodeDto> nodes, CancellationToken ct = default);

    Task UpsertEdgeAsync(ScanHandle scan, EdgeDto edge, CancellationToken ct = default);

    Task UpsertEdgesAsync(ScanHandle scan, IReadOnlyCollection<EdgeDto> edges, CancellationToken ct = default);

    /// <summary>
    /// Marks the scan as complete. For a Full scan, any node/edge whose scan_version
    /// is older than this scan and belongs to this repo_id is considered stale and is
    /// soft-deleted (Phase 3+) or hard-deleted (Phase 1).
    /// </summary>
    Task CompleteScanAsync(ScanHandle scan, CancellationToken ct = default);

    Task FailScanAsync(ScanHandle scan, string errorMessage, CancellationToken ct = default);
}

public sealed record BeginScanRequest
{
    public string RepoId { get; init; } = "default";
    public required ScanType ScanType { get; init; }     // Full | Incremental
    public string? TriggeredBy { get; init; }             // "cli" | "watcher" | "ci"
    public IReadOnlyCollection<string>? ChangedFiles { get; init; } // required for Incremental
}

public enum ScanType { Full, Incremental }

public sealed record ScanHandle
{
    public required long ScanRunId { get; init; }
    public required string RepoId { get; init; }
    public required ScanType ScanType { get; init; }
}
```

### 4.3 `IGraphWriter` — Phase 3 Additions (incremental precision)

```csharp
public interface IIncrementalGraphWriter : IGraphWriter
{
    /// <summary>
    /// Deletes (soft-deletes, if versioning enabled) all nodes and edges whose FilePath
    /// matches any of the given paths and whose scan_version is older than `scan`.
    /// Called by the Incremental Watcher BEFORE re-upserting nodes/edges parsed from
    /// those files, so stale symbols removed from a file don't linger.
    /// </summary>
    Task InvalidateByFilePathAsync(ScanHandle scan, IReadOnlyCollection<string> filePaths, CancellationToken ct = default);

    /// <summary>
    /// Explicit delete for a node no longer present anywhere (e.g. file deleted entirely).
    /// Cascades to edges referencing the node.
    /// </summary>
    Task DeleteNodeAsync(ScanHandle scan, string nodeId, CancellationToken ct = default);

    /// <summary>Computes and persists snapshot delta (Architecture Timeline) after CompleteScanAsync.</summary>
    Task<SnapshotDto> RecordSnapshotAsync(ScanHandle scan, CancellationToken ct = default);
}
```

### 4.4 Writer Contract Semantics (must-follow rules for the Scanner team)

1. **ID determinism**: `NodeId`/`EdgeId`/`ProjectId` MUST be computed via `IIdGenerator` (SHA-1 of a stable composite key), never a random GUID. This is what makes upserts idempotent across scans.
2. **Ordering**: Call `UpsertProjectAsync` for all projects before `UpsertNodesAsync`, and all nodes before `UpsertEdgesAsync`, within a scan — the Graph Store does not defer FK validation.
3. **One `ScanHandle` per scan**: never share a `ScanHandle` across concurrent scans of the same `repo_id`; the store enforces a single in-flight `Running` scan per `repo_id` and will throw `ScanConflictException` otherwise.
4. **Metadata dictionary is the escape hatch**: anything not modeled as a first-class column (HTTP verb, DI lifetime, MediatR request/response types, EF navigation cardinality) goes into `Metadata`. The Graph Store stores it as JSON and does not validate its shape in Phase 1–2. Phase 3+ MAY introduce known metadata keys used by metrics computation (documented in a shared `MetadataKeys` static class).
5. **Full scan = authoritative**: after `CompleteScanAsync` on a `Full` scan, anything not touched by that scan (same `repo_id`) is considered gone and is removed (soft-deleted from Phase 3 onward).
6. **Incremental scan = surgical**: the watcher must call `InvalidateByFilePathAsync` for every changed file before re-upserting, so renamed/removed symbols within an edited file are cleaned up.

---

## 5. Reader/Query Contract

This is the contract consumed by the **REST API**, **MCP Server**, and **CLI**. All three are thin adapters over `IGraphReader` — no SQL or storage detail leaks past this interface.

```csharp
namespace ArchIntel.GraphStore.Contracts;

public interface IGraphReader
{
    // ---- Phase 1: basic lookups ----
    Task<NodeDto?> GetNodeAsync(string nodeId, CancellationToken ct = default);
    Task<IReadOnlyList<NodeDto>> FindByNameAsync(string name, NodeType? nodeType = null, bool exactMatch = false, CancellationToken ct = default);
    Task<IReadOnlyList<ProjectDto>> ListProjectsAsync(string repoId = "default", CancellationToken ct = default);
    Task<IReadOnlyList<NodeDto>> GetNodesByProjectAsync(string projectId, NodeType? nodeType = null, CancellationToken ct = default);

    /// <summary>Direct (1-hop) outgoing dependencies of a node, optionally filtered by relationship type.</summary>
    Task<IReadOnlyList<EdgeWithNodeDto>> GetDependenciesAsync(string nodeId, RelationshipType? relationshipType = null, CancellationToken ct = default);

    /// <summary>Direct (1-hop) incoming edges — i.e. who depends on / calls this node.</summary>
    Task<IReadOnlyList<EdgeWithNodeDto>> GetCallersAsync(string nodeId, RelationshipType? relationshipType = null, CancellationToken ct = default);

    // ---- Phase 2: subgraph extraction & rendering support ----

    /// <summary>Transitive impact set: everything reachable FROM nodeId within maxDepth hops, following the given relationship types (default: all).</summary>
    Task<ImpactResultDto> GetImpactAsync(string nodeId, int maxDepth = 10, IReadOnlyCollection<RelationshipType>? relationshipTypes = null, CancellationToken ct = default);

    /// <summary>Transitive dependents: everything that can reach nodeId within maxDepth hops (reverse traversal). Used for "what breaks if I change this".</summary>
    Task<ImpactResultDto> GetTransitiveDependentsAsync(string nodeId, int maxDepth = 10, IReadOnlyCollection<RelationshipType>? relationshipTypes = null, CancellationToken ct = default);

    /// <summary>Extracts a renderable subgraph (nodes + edges) around a seed node, for Cytoscape/Sigma/React Flow consumption.</summary>
    Task<SubgraphDto> GetNeighborhoodAsync(GetNeighborhoodRequest request, CancellationToken ct = default);

    /// <summary>Extracts a full or filtered subgraph for a project/set of projects (e.g. "show me the whole Business layer").</summary>
    Task<SubgraphDto> GetSubgraphAsync(GetSubgraphRequest request, CancellationToken ct = default);

    /// <summary>Finds all simple paths between two nodes, up to maxDepth — used for "how does A reach B" queries and diagram generation.</summary>
    Task<IReadOnlyList<PathDto>> FindPathsAsync(string sourceNodeId, string targetNodeId, int maxDepth = 8, CancellationToken ct = default);

    // ---- Phase 3: metrics, cycles, history ----

    Task<IReadOnlyList<NodeMetricDto>> GetNodeMetricsAsync(string nodeId, int? scanRunId = null, CancellationToken ct = default);
    Task<IReadOnlyList<ProjectMetricDto>> GetProjectMetricsAsync(string? projectId = null, int? scanRunId = null, CancellationToken ct = default);
    Task<IReadOnlyList<CircularDependencyDto>> GetCircularDependenciesAsync(string? projectId = null, CancellationToken ct = default);
    Task<IReadOnlyList<SnapshotDto>> GetTimelineAsync(DateTimeOffset? since = null, int limit = 50, CancellationToken ct = default);
    Task<SnapshotDto?> GetLatestSnapshotAsync(string repoId = "default", CancellationToken ct = default);

    // ---- Phase 4: multi-repo, quality scoring ----

    Task<IReadOnlyList<RepositoryDto>> ListRepositoriesAsync(CancellationToken ct = default);
    Task<ArchitectureQualityScoreDto?> GetQualityScoreAsync(string repoId, int? scanRunId = null, CancellationToken ct = default);
}
```

### 5.1 Supporting Query DTOs

```csharp
public sealed record EdgeWithNodeDto
{
    public required EdgeDto Edge { get; init; }
    public required NodeDto OtherNode { get; init; }  // the target (for GetDependencies) or source (for GetCallers)
}

public sealed record ImpactResultDto
{
    public required string RootNodeId { get; init; }
    public required IReadOnlyList<NodeDto> AffectedNodes { get; init; }
    public required IReadOnlyList<PathDto> SamplePaths { get; init; }   // representative paths for explanation/UI
    public required IReadOnlyDictionary<NodeType, int> AffectedByType { get; init; } // e.g. {API: 3, Repository: 1, Tests: 5}
}

public sealed record GetNeighborhoodRequest
{
    public required string SeedNodeId { get; init; }
    public int Depth { get; init; } = 1;
    public IReadOnlyCollection<RelationshipType>? RelationshipTypes { get; init; }
    public IReadOnlyCollection<NodeType>? NodeTypes { get; init; }   // filter by node type
    public bool IncludeExternal { get; init; } = true;
    public int MaxNodes { get; init; } = 500;   // safety cap for large-graph rendering
}

public sealed record GetSubgraphRequest
{
    public IReadOnlyCollection<string>? ProjectIds { get; init; }
    public IReadOnlyCollection<NodeType>? NodeTypes { get; init; }
    public IReadOnlyCollection<RelationshipType>? RelationshipTypes { get; init; }
    public int MaxNodes { get; init; } = 2000;
    public int Page { get; init; } = 0;
    public int PageSize { get; init; } = 500;
}

public sealed record SubgraphDto
{
    public required IReadOnlyList<NodeDto> Nodes { get; init; }
    public required IReadOnlyList<EdgeDto> Edges { get; init; }
    public bool Truncated { get; init; }   // true if MaxNodes was hit
}

public sealed record PathDto
{
    public required IReadOnlyList<string> NodeIds { get; init; }
    public required IReadOnlyList<string> EdgeIds { get; init; }
}

public sealed record NodeMetricDto
{
    public required string NodeId { get; init; }
    public int FanIn { get; init; }
    public int FanOut { get; init; }
    public double? CouplingScore { get; init; }
    public double? ComplexityScore { get; init; }
    public required int ScanRunId { get; init; }
}

public sealed record ProjectMetricDto
{
    public required string ProjectId { get; init; }
    public int AfferentCoupling { get; init; }
    public int EfferentCoupling { get; init; }
    public double Instability { get; init; }
    public int NodeCount { get; init; }
    public required int ScanRunId { get; init; }
}

public sealed record CircularDependencyDto
{
    public required string CycleId { get; init; }
    public required IReadOnlyList<string> NodeIds { get; init; }
    public int CycleLength { get; init; }
    public string? Severity { get; init; }
}

public sealed record SnapshotDto
{
    public required int SnapshotId { get; init; }
    public required DateTimeOffset TakenAt { get; init; }
    public int TotalProjects { get; init; }
    public int TotalNodes { get; init; }
    public int TotalEdges { get; init; }
    public int NodesAdded { get; init; }
    public int NodesRemoved { get; init; }
    public int NodesModified { get; init; }
    public int EdgesAdded { get; init; }
    public int EdgesRemoved { get; init; }
    public int ProjectsAdded { get; init; }
    public int ProjectsRemoved { get; init; }
    public IReadOnlyDictionary<string, object> Summary { get; init; } = new Dictionary<string, object>();
}

public sealed record RepositoryDto
{
    public required string RepoId { get; init; }
    public required string Name { get; init; }
    public string? RemoteUrl { get; init; }
    public DateTimeOffset? LastSyncedAt { get; init; }
}

public sealed record ArchitectureQualityScoreDto
{
    public required string RepoId { get; init; }
    public required double OverallScore { get; init; }
    public double? CouplingComponent { get; init; }
    public double? CyclicalityComponent { get; init; }
    public double? LayeringComponent { get; init; }
    public double? TestCoverageComponent { get; init; }
}
```

### 5.2 Representative Query Implementations (SQLite, Dapper)

```csharp
// GetImpactAsync — recursive CTE over edges, forward direction, depth-limited.
const string ImpactSql = @"
WITH RECURSIVE impact(node_id, depth) AS (
    SELECT @NodeId, 0
    UNION
    SELECT e.target_id, i.depth + 1
    FROM edges e
    JOIN impact i ON e.source_id = i.node_id
    WHERE i.depth < @MaxDepth
      AND e.is_deleted = 0
      AND (@RelTypesNull = 1 OR e.relationship_type IN @RelTypes)
)
SELECT DISTINCT n.*
FROM nodes n
JOIN impact i ON n.node_id = i.node_id
WHERE n.node_id != @NodeId AND n.is_deleted = 0;
";

// Circular dependency detection at the PROJECT level (Phase 3) — find cycles in the
// project reference graph. Implemented in application code (Tarjan's SCC algorithm)
// after pulling the full project-level edge list, rather than pure SQL, because
// SQLite has no native graph-cycle primitive and the project-level graph is small
// enough (hundreds, not millions, of edges) to process in memory safely.
```

Note on cycle detection: **node-level** cycle detection (e.g. two classes calling each other) reuses the same in-memory Tarjan's SCC approach but is bounded by `MaxNodes`/timeouts to avoid pathological cost on very large graphs; if this becomes a bottleneck, it is one of the concrete motivations for the optional Neo4j backend in Phase 4 (`CALL algo.scc` semantics).

### 5.3 CLI / MCP / REST Mapping (illustrative, not exhaustive)

| Consumer surface | Reader method |
|---|---|
| `arch explain OrderService` | `FindByNameAsync` → `GetDependenciesAsync` + `GetCallersAsync` |
| `arch impact ModelVersion` | `GetImpactAsync` |
| `arch callers IRepository` | `GetCallersAsync` |
| `arch diagram Business` | `GetSubgraphAsync` (ProjectIds filter) → Mermaid exporter |
| `arch metrics` | `GetProjectMetricsAsync`, `GetNodeMetricsAsync` |
| MCP `find_dependencies()` | `GetDependenciesAsync` |
| MCP `find_callers()` | `GetCallersAsync` |
| MCP `impact_analysis()` | `GetImpactAsync` |
| MCP `find_service()` | `FindByNameAsync` |
| MCP `generate_diagram()` | `GetSubgraphAsync` / `GetNeighborhoodAsync` → Mermaid exporter |
| REST `GET /graph` | `GetSubgraphAsync` |
| REST `GET /impact` | `GetImpactAsync` |
| REST `GET /metrics` | `GetProjectMetricsAsync` |

---

## 6. Incremental Update & Versioning Strategy (Phase 3)

### 6.1 Update Flow

1. Incremental Watcher detects changed files (via `FileSystemWatcher` / git diff).
2. Watcher calls `BeginScanAsync(new BeginScanRequest { ScanType = Incremental, ChangedFiles = [...] })`.
3. Watcher calls `InvalidateByFilePathAsync(scan, changedFiles)` — this closes the validity window (`valid_to = now`, `is_deleted = 1` conceptually via the window) of all nodes/edges whose `file_path` is in the changed set **and** whose `scan_version` predates this scan.
4. Scanner re-parses only the changed files (and, per the Scanner's own dependency-resolution rules, anything referencing them) and calls `UpsertNodesAsync` / `UpsertEdgesAsync` as usual — these create new rows with a fresh `valid_from` and the new `scan_version`, reusing the same deterministic `node_id`/`edge_id` if the symbol still exists (so it looks like an update, not delete+insert, from a history perspective).
5. Watcher calls `CompleteScanAsync(scan)`.
6. Graph Store calls `RecordSnapshotAsync(scan)` internally (or the watcher calls it explicitly) to diff against the previous snapshot and populate the `snapshots` table for the Timeline UI.

### 6.2 Versioning Semantics

* Every node/edge row is **never physically updated in place** for a value change once versioning is active — instead, the previous row is closed (`valid_to = now`) and a new row is inserted with the same `node_id`/`edge_id` but a new `(valid_from, valid_to=NULL)` window. This means `node_id` is **not** a strict primary key once versioning is on; the true row identity is `(node_id, valid_from)`.
  * *(Implementation note: Phase 1/2 schema above shows `node_id`/`edge_id` as `PRIMARY KEY` for simplicity; the Phase 3 migration alters this to a composite key `(node_id, valid_from)` with a partial unique index enforcing at most one row per `node_id` where `valid_to IS NULL`, representing "current".)*
* All Reader queries **default to `valid_to IS NULL`** (current state only) unless a caller explicitly asks for historical/point-in-time queries (not exposed in the public contract until there's a concrete consumer need — the column exists to make it possible later without a schema change).
* Hard-delete retention job (configurable, default 90 days) purges rows where `valid_to < now - retention`.

### 6.3 Snapshot Delta Computation

`RecordSnapshotAsync` computes, by comparing the current scan's touched node/edge sets against the previous `Completed` scan for the same `repo_id`:

* `nodes_added` = count of node_ids present now, absent in previous current-set
* `nodes_removed` = count of node_ids present in previous current-set, absent now
* `nodes_modified` = count of node_ids present in both, but with a materially different row (name/namespace/metadata hash changed)
* Same for edges and projects
* `summary_json` breaks this down per `NodeType` (e.g. `{"Class": {"added": 28, "removed": 0}, "Interface": {"added": 0, "removed": 1}, "Project": {"added": 3, "removed": 0}}`) — this is exactly the data the README's Timeline example (`+28 classes`, `+3 projects`, `-1 interface`) needs.

### 6.4 Metrics Computation Trigger

`node_metrics` / `project_metrics` / `circular_dependencies` are computed as a **post-scan step** (not inline during upsert), triggered by `CompleteScanAsync`:

1. Fan-in/fan-out per node = count of current, non-deleted incoming/outgoing edges.
2. Project-level afferent/efferent coupling = count of distinct *other* projects referencing/referenced-by this project's nodes.
3. Circular dependency detection runs Tarjan's SCC over the current project-reference subgraph (and optionally a node-level pass, bounded).
4. All results stamped with the current `scan_run_id` so metrics history is queryable over time (feeds the Coupling Heatmap and its trend view).

This is implemented as an internal `IMetricsCalculator` service inside the Graph Store module (not part of the public Writer/Reader contract), invoked by `CompleteScanAsync`, so Scanner/Watcher never need to know metrics exist.

---

## 7. Multi-backend / Migration Strategy (Phase 4)

### 7.1 Backend Abstraction

```csharp
public enum GraphBackend { Sqlite, Postgres, Neo4j }

public interface IGraphStoreFactory
{
    IGraphWriter CreateWriter(GraphStoreOptions options);
    IGraphReader CreateReader(GraphStoreOptions options);
}

public sealed record GraphStoreOptions
{
    public required GraphBackend Backend { get; init; }
    public required string ConnectionString { get; init; }
    public string RepoId { get; init; } = "default";
}
```

Consumers (Scanner, Watcher, MCP Server, REST API) are configured with `GraphStoreOptions` (from `arch.config.yaml` / environment) and never instantiate `SqliteGraphWriter` or `PostgresGraphWriter` directly — always through `IGraphStoreFactory`.

### 7.2 SQLite → PostgreSQL Migration Tool

`arch migrate --from sqlite --to postgres --connection "<conn-string>"`:

1. Opens both backends via the factory.
2. Streams data table-by-table in dependency order: `repositories` → `projects` → `nodes` → `edges` → `scan_runs` → `node_metrics`/`project_metrics`/`circular_dependencies` → `snapshots`.
3. Uses batched `UpsertNodesAsync`/`UpsertEdgesAsync` (already idempotent) rather than raw `INSERT`, so a failed/resumed migration is safe to re-run.
4. Validates row counts and a checksum (e.g. count + max(updated_at) per table) match between source and destination before declaring success.
5. Leaves the SQLite file untouched (non-destructive) so it can be used as a local cache/fallback (e.g. offline CLI usage) even after the "primary" store is Postgres.

### 7.3 PostgreSQL Schema Notes

* Same table/column names as SQLite where practical, to minimize divergence in Dapper SQL (only the recursive CTE and upsert/`ON CONFLICT` syntax differ meaningfully).
* `metadata_json` becomes `JSONB` with a `GIN` index to support future metadata-based filtering (e.g. "find all controllers with HTTP verb POST") without new columns.
* Connection pooling via Npgsql's built-in pooler; the Graph Store module does not manage its own pool.
* Postgres unlocks concurrent writers (multiple incremental watchers across a team, cloud sync jobs) — SQLite's single-writer lock is a known Phase 1–3 limitation, explicitly called out in §10.

### 7.4 Embeddings Table (adjacent, not core graph data)

Even though the Graph Store's core is relational/graph, Phase 4 co-locates a documentation/semantic-search table in the same Postgres instance for operational simplicity:

```sql
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE doc_embeddings (
    embedding_id    TEXT PRIMARY KEY,
    repo_id         TEXT NOT NULL REFERENCES repositories(repo_id),
    node_id          TEXT REFERENCES nodes(node_id),   -- nullable: some embeddings are for free-text docs, not a specific node
    source_type      TEXT NOT NULL,                     -- 'xmldoc' | 'markdown' | 'readme' | 'commit_message'
    source_ref        TEXT,                              -- file path or URL
    content_hash       TEXT NOT NULL,
    embedding           VECTOR(1536),                     -- OpenAI text-embedding-3-small dimension
    created_at           TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_doc_embeddings_vector ON doc_embeddings USING ivfflat (embedding vector_cosine_ops);
```

The Graph Store exposes a narrow, clearly-separated `ISemanticSearchReader` (not part of `IGraphReader`) so it's obvious to consumers that semantic search is a different reasoning mode than graph traversal:

```csharp
public interface ISemanticSearchReader
{
    Task<IReadOnlyList<SemanticMatchDto>> SearchAsync(string queryEmbeddingOwnerText, int topK = 10, CancellationToken ct = default);
}
```

The Graph Store does **not** call OpenAI itself; a separate indexing pipeline (outside this component's scope, likely part of the Scanner or a dedicated indexer) computes embeddings and writes them via a corresponding `ISemanticSearchWriter`. This is called out here only because the table lives in the same physical database and the migration tooling must account for it.

### 7.5 Neo4j Adapter (optional)

* `Neo4jGraphWriter`/`Neo4jGraphReader` implement the same interfaces using the official `Neo4j.Driver` package, translating upserts into `MERGE` statements keyed by `node_id`/`edge_id` (stored as a property, since Neo4j's internal IDs are not stable across compaction).
* Positioned as an **alternative to**, not a replacement for, Postgres — a repo owner opts in via `GraphStoreOptions.Backend = Neo4j` when graph size/traversal depth justifies the operational cost.
* Explicitly out of scope for the Phase 4 committed deliverable list; tracked as a stretch goal / spike.

---

## 8. Project/Module Structure

```
src/
  GraphStore/
    ArchIntel.GraphStore.Contracts/         # Phase 1 — pure interfaces + DTOs, zero storage dependencies
      NodeDto.cs
      EdgeDto.cs
      ProjectDto.cs
      Enums/
        NodeType.cs
        RelationshipType.cs
      IGraphWriter.cs
      IIncrementalGraphWriter.cs
      IGraphReader.cs
      ISemanticSearchReader.cs
      IIdGenerator.cs
      GraphStoreOptions.cs
      Exceptions/
        ScanConflictException.cs
        NodeNotFoundException.cs

    ArchIntel.GraphStore.Core/              # Phase 1 — shared logic independent of backend
      IdGenerator.cs                         # SHA-1 based deterministic ID generation
      MetricsCalculator/                      # Phase 3
        IMetricsCalculator.cs
        CouplingCalculator.cs
        CycleDetector.cs                       # Tarjan's SCC
        SnapshotDiffCalculator.cs
      GraphStoreFactory.cs                     # Phase 4 — backend switch

    ArchIntel.GraphStore.Sqlite/              # Phase 1
      SqliteConnectionFactory.cs
      SqliteGraphWriter.cs
      SqliteGraphReader.cs
      Migrations/
        001_init.sql
        002_phase2_indices.sql
        003_phase3_metrics_and_history.sql
      DapperTypeHandlers/
        NodeTypeHandler.cs
        RelationshipTypeHandler.cs
        MetadataJsonHandler.cs

    ArchIntel.GraphStore.Postgres/            # Phase 4
      NpgsqlConnectionFactory.cs
      PostgresGraphWriter.cs
      PostgresGraphReader.cs
      Migrations/
        004_phase4_multirepo_and_scoring.sql
        005_postgres_jsonb_and_vector.sql

    ArchIntel.GraphStore.Neo4j/               # Phase 4 (optional/experimental)
      Neo4jConnectionFactory.cs
      Neo4jGraphWriter.cs
      Neo4jGraphReader.cs
      CypherQueries.cs

    ArchIntel.GraphStore.Migration/            # Phase 4 — cross-backend migration tool
      SqliteToPostgresMigrator.cs
      MigrationValidator.cs

  Cli/
    ArchIntel.Cli/                              # consumes IGraphReader/IGraphWriter via DI, not directly referenced here

tests/
  GraphStore/
    ArchIntel.GraphStore.Sqlite.Tests/
    ArchIntel.GraphStore.Postgres.Tests/         # Phase 4, Testcontainers-based
    ArchIntel.GraphStore.Contracts.Tests/        # contract/shared test suite run against every backend
    ArchIntel.GraphStore.Core.Tests/
```

Key structural decision: **`Contracts` has zero dependency on Dapper, SQLite, Postgres, or Neo4j packages.** Scanner and Watcher projects reference only `ArchIntel.GraphStore.Contracts`. The concrete backend package is wired up only at the composition root (CLI `Program.cs` / API `Startup`), via `IGraphStoreFactory`.

---

## 9. Testing Strategy

### 9.1 Contract Test Suite (the most important tests in this component)

A single shared test suite (`ArchIntel.GraphStore.Contracts.Tests`) is written **once** against the interfaces and run against **every backend** (SQLite in Phase 1+, Postgres and Neo4j in Phase 4) via a parameterized fixture. This guarantees behavioral parity across backends — the exact guarantee the Writer/Reader contract promises to Scanner/Watcher/API/MCP teams.

Representative cases:

* Upserting the same `NodeDto` twice with different metadata results in one row with updated metadata (idempotency).
* `CompleteScanAsync` on a `Full` scan removes nodes/edges not touched by that scan.
* `InvalidateByFilePathAsync` + re-upsert correctly replaces symbols from a changed file without touching unrelated files' nodes.
* `GetImpactAsync` respects `maxDepth` and `relationshipTypes` filters exactly (off-by-one depth is the classic bug here).
* `GetDependenciesAsync`/`GetCallersAsync` return correct 1-hop results and empty lists (not null) for leaf/root nodes.
* Circular dependency detection finds a known 3-node cycle fixture and does not false-positive on a DAG fixture.
* Snapshot delta math matches hand-computed expected values for a scripted two-scan scenario (add 2 classes, remove 1 interface, modify 1 method signature).
* Concurrent `BeginScanAsync` for the same `repo_id` throws `ScanConflictException`.

### 9.2 Unit Tests

* `IIdGenerator` — deterministic and collision-resistant across realistic name variations (generic types, nested classes, overloads).
* `MetricsCalculator` components (coupling formulas, Tarjan's SCC) tested with hand-built small graphs where the correct answer is known analytically.
* `SnapshotDiffCalculator` tested in isolation with mocked before/after node sets.

### 9.3 Integration Tests

* SQLite: in-memory (`Data Source=:memory:`) or temp-file database per test class; migrations applied fresh each run.
* Postgres (Phase 4): Testcontainers spinning up a real Postgres instance so `JSONB`/`ON CONFLICT`/recursive CTE syntax is validated against the real engine, not assumptions.
* Migration tool: round-trip test — seed SQLite with a realistic fixture graph (hundreds of nodes/edges across multiple projects), run `arch migrate`, assert Postgres reader returns identical `GetSubgraphAsync`/`GetImpactAsync` results as the SQLite reader did pre-migration.

### 9.4 Performance / Load Tests

* Synthetic large-graph fixture generator (configurable node/edge count, e.g. 50k nodes / 200k edges) to validate:
  * Full scan upsert throughput (target: complete within a few seconds to low minutes for a 50k-node repo on SQLite).
  * `GetImpactAsync`/`GetNeighborhoodAsync` latency stays interactive (sub-second to low-seconds) at realistic `maxDepth`/`MaxNodes` caps — this directly gates the Phase 2 interactive dashboard's usability.
  * SQLite single-writer contention under a scan running concurrently with API read traffic (WAL mode should be enabled; validate no reader starvation).

### 9.5 Backward-Compatibility / Contract-Change Tests

* Whenever the `Contracts` project changes, a test asserts the public interface's method signatures against a recorded "approved" snapshot (e.g. via a source-generator or reflection-based API surface diff), so accidental breaking changes to `IGraphWriter`/`IGraphReader` fail CI loudly and require explicit sign-off from the Scanner team's plan (`01-architecture-scanner.md`).

---

## 10. Risks & Open Questions

| # | Risk / Question | Notes / Mitigation |
|---|---|---|
| 1 | **SQLite single-writer contention** during a scan while API/MCP server is reading. | Use WAL mode (`journal_mode=WAL`) from Phase 1; readers don't block on writer in WAL mode. Revisit if Phase 2 dashboard usage under active scanning proves problematic. |
| 2 | **Deterministic ID collisions** for generic types, partial classes, overloaded methods, or nested/local functions. | `IIdGenerator` must incorporate enough of the symbol's fully qualified signature (including generic arity, parameter types for methods) to avoid collisions; needs a dedicated edge-case test suite co-designed with the Scanner team, since Roslyn symbol formatting nuances live on their side. |
| 3 | **Recursive CTE performance** on deep/wide graphs (e.g. `GetImpactAsync` at `maxDepth=10` over 50k+ nodes). | Cap `maxDepth` and result size (`MaxNodes`) at the contract level; add query timeouts; this is the primary motivator for the optional Neo4j backend if it becomes a real bottleneck. |
| 4 | **Versioning schema migration (Phase 3)** changes the primary key shape (`node_id` → `(node_id, valid_from)`), which is a non-trivial migration on data already in production/dogfood use. | Plan the Phase 3 migration script carefully; consider whether versioning should actually be designed in from Phase 1 schema (accepting the complexity earlier) rather than retrofitted — **open question, needs a decision before Phase 1 schema is finalized**. |
| 5 | **What exactly counts as "modified" for snapshot delta / Timeline reporting?** Any metadata change, or only "meaningful" changes (rename, signature change)? | Needs product input; default proposal is: hash of `(name, full_name, namespace, node_type, sorted metadata)` — any change to that hash = modified. |
| 6 | **Metadata dictionary as a schema escape hatch** risks becoming a dumping ground with inconsistent keys across scanner versions. | Introduce a shared `MetadataKeys` constants class (owned jointly with Scanner team) and a lightweight schema/lint check in CI that flags unknown metadata keys written by the Scanner. |
| 7 | **Multi-repo `repo_id` performance** — every table/index includes `repo_id`, but Phase 1–3 always use `'default'`, so real-world index selectivity for `repo_id` is untested until Phase 4. | Include `repo_id` in composite indices from Phase 1 (already reflected in §3.2) so Phase 4 doesn't require an index redesign, only population of real values. |
| 8 | **Neo4j adapter scope creep.** | Explicitly optional/experimental (§7.5, §7.7) — do not let it block the committed Phase 4 Postgres migration. |
| 9 | **Embeddings table co-location** — should `doc_embeddings` really live in the same physical Postgres instance as the graph, or a separate service? | Proposal: same instance for Phase 4 simplicity (fewer moving parts for self-hosted users), but behind a distinct `ISemanticSearchReader`/`Writer` so it can be split into a separate service later without touching graph consumers. |
| 10 | **Circular dependency detection cost at node level** on very large graphs. | Bound with timeouts and a "project-level only" default, with node-level cycle detection as an opt-in, slower, deeper analysis (`arch metrics --deep`). |
| 11 | **Concurrent incremental watchers** (e.g. two developers running `arch watch` against a shared Postgres backend in Phase 4). | `ScanConflictException` handles same-`repo_id` concurrent scans, but cross-machine incremental watching against a shared cloud store needs a conflict/merge story — flagged for Phase 4 design, not solved here. |
| 12 | **DbUp vs FluentMigrator vs hand-rolled migration runner** — not yet spiked. | Small decision, low risk either way; resolve in Phase 1 Task Breakdown (§11) before writing migration scripts for real. |

---

## 11. Task Breakdown

### Phase 1 — SQLite Storage, Basic Read/Write

- [ ] Spike & decide migration runner (DbUp vs FluentMigrator vs custom) — resolves Risk #12
- [ ] Create `ArchIntel.GraphStore.Contracts` project: enums (`NodeType`, `RelationshipType`), DTOs (`NodeDto`, `EdgeDto`, `ProjectDto`), `IIdGenerator`
- [ ] Define `IGraphWriter` and `IGraphReader` interfaces (Phase 1 subset only)
- [ ] Implement `IIdGenerator` (SHA-1 deterministic hashing) with unit tests covering generic types, overloads, nested classes
- [ ] Write `001_init.sql` migration (projects, nodes, edges, scan_runs tables + Phase 1 indices)
- [ ] Implement `SqliteConnectionFactory` with WAL mode enabled
- [ ] Implement `SqliteGraphWriter`: `BeginScanAsync`, `UpsertProjectAsync`, `UpsertNodeAsync`/`UpsertNodesAsync`, `UpsertEdgeAsync`/`UpsertEdgesAsync`, `CompleteScanAsync` (incl. stale-row cleanup for Full scans), `FailScanAsync`
- [ ] Implement `SqliteGraphReader`: `GetNodeAsync`, `FindByNameAsync`, `ListProjectsAsync`, `GetNodesByProjectAsync`, `GetDependenciesAsync`, `GetCallersAsync`
- [ ] Dapper type handlers for `NodeType`/`RelationshipType` enums and `metadata_json` dictionary (de)serialization
- [ ] Contract test suite v1 (idempotent upsert, full-scan stale cleanup, basic dependency/caller queries)
- [ ] Wire into CLI: `arch scan` (full scan via `IGraphWriter`), `arch explain <name>` (via `IGraphReader`)
- [ ] Wire into basic MCP server: `find_dependencies()`, `find_callers()`, `find_service()`
- [ ] Document the frozen Writer/Reader contract for the Scanner team (README in `ArchIntel.GraphStore.Contracts` or shared wiki page)

### Phase 2 — Interactive Graph & Impact Analysis Support

- [ ] Add `002_phase2_indices.sql` (composite indices for neighborhood/subgraph queries)
- [ ] Implement `GetImpactAsync` and `GetTransitiveDependentsAsync` (recursive CTEs) with `maxDepth` + `relationshipTypes` filtering
- [ ] Implement `GetNeighborhoodAsync` (bounded neighborhood extraction with `MaxNodes` cap and `Truncated` flag)
- [ ] Implement `GetSubgraphAsync` with project/node-type/relationship-type filtering and pagination
- [ ] Implement `FindPathsAsync` (bounded simple-path search)
- [ ] Add Mermaid/DOT export helper consuming `SubgraphDto` (used by `arch diagram` and MCP `generate_diagram()`)
- [ ] Load/perf test subgraph and impact queries against a synthetic 10k–50k node fixture; tune indices
- [ ] Wire REST API `GET /graph`, `GET /impact` endpoints against the new reader methods
- [ ] Wire Dashboard's Cytoscape/Sigma/React Flow views against `GetSubgraphAsync`/`GetNeighborhoodAsync`
- [ ] Extend contract test suite with impact/neighborhood/subgraph/path cases

### Phase 3 — Incremental Updates, Metrics, Timeline

- [ ] Design & migrate versioning schema change (`(node_id, valid_from)` composite identity, partial unique index on current rows) — resolve Risk #4 first
- [ ] Add `is_deleted`/`valid_from`/`valid_to` activation logic to writer (soft-delete instead of hard-delete)
- [ ] Implement `IIncrementalGraphWriter`: `InvalidateByFilePathAsync`, `DeleteNodeAsync`, `RecordSnapshotAsync`
- [ ] Add `003_phase3_metrics_and_history.sql` (`node_metrics`, `project_metrics`, `circular_dependencies`, `snapshots`)
- [ ] Implement `IMetricsCalculator`: fan-in/fan-out, project afferent/efferent coupling + instability
- [ ] Implement `CycleDetector` (Tarjan's SCC) at project level (default) and node level (opt-in, bounded)
- [ ] Implement `SnapshotDiffCalculator` and wire into `RecordSnapshotAsync`, producing the `+28 classes / +3 projects / -1 interface` style summary
- [ ] Implement reader methods: `GetNodeMetricsAsync`, `GetProjectMetricsAsync`, `GetCircularDependenciesAsync`, `GetTimelineAsync`, `GetLatestSnapshotAsync`
- [ ] Wire Incremental Watcher (`arch watch`) against `IIncrementalGraphWriter`
- [ ] Wire REST `GET /metrics`, Dashboard Coupling Heatmap and Architecture Timeline views
- [ ] Retention/purge job for hard-deleting rows past `valid_to` + retention window
- [ ] Extend contract test suite with incremental-invalidation, versioning, metrics, and snapshot-delta cases
- [ ] Load test: incremental update latency for a single-file change in a large repo (target: near-instant, sub-second graph-side processing)

### Phase 4 — Multi-Backend, Multi-Repo, Cloud Sync

- [ ] Introduce `IGraphStoreFactory` / `GraphStoreOptions` / `GraphBackend` enum; refactor CLI/API composition roots to use it
- [ ] Add `004_phase4_multirepo_and_scoring.sql` (`repositories`, `architecture_quality_scores`, `sync_log`) to SQLite path
- [ ] Populate real `repo_id` values for multi-repo scans (data migration from `'default'`)
- [ ] Implement `ArchIntel.GraphStore.Postgres`: `PostgresGraphWriter`, `PostgresGraphReader`, Postgres-flavored migrations (`JSONB`, `ON CONFLICT`, recursive CTEs)
- [ ] Implement `SqliteToPostgresMigrator` + `MigrationValidator`; add `arch migrate` CLI command
- [ ] Testcontainers-based Postgres integration test suite; run full contract test suite against Postgres
- [ ] Implement `architecture_quality_scores` computation (coupling + cyclicality + layering + test-coverage components) and `GetQualityScoreAsync`
- [ ] Design and implement cloud sync (`sync_log`, push/pull semantics) — needs its own detailed design pass, flagged as a sub-plan if scope grows
- [ ] pgvector `doc_embeddings` table + `ISemanticSearchReader`/`Writer`; confirm migration tool accounts for this table
- [ ] (Stretch/experimental) Spike `ArchIntel.GraphStore.Neo4j`: `Neo4jGraphWriter`/`Reader`, Cypher translations of `GetImpactAsync`/cycle detection, run contract test suite against it
- [ ] Update Writer/Reader contract docs and MCP/REST/CLI consumers for any Phase 4 additions (`ListRepositoriesAsync`, `GetQualityScoreAsync`)
