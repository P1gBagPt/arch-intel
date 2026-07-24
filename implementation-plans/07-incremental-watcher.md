# 07 — Incremental Watcher: Implementation Plan

Component: **Incremental Watcher**
CLI entry point: `arch watch`
Depends on: Architecture Scanner (`01-architecture-scanner.md`), Graph Store (`02-graph-store.md`), REST API / SignalR Hub (`05-rest-api.md`)
Consumed by: Next.js Dashboard (Architecture Timeline, Live Updates), MCP Server sessions, CI/local dev loop

---

## 1. Overview & Responsibilities

The Incremental Watcher is the component that turns the Architecture Intelligence Platform from a "run it and get a snapshot" tool into a system that keeps a **continuously current** architecture graph. Instead of re-scanning an entire solution on every invocation, the watcher:

1. **Detects changed files** on disk (source files, project files, config files) using a file-system event source, normalized and debounced.
2. **Computes the blast radius** — the set of graph nodes whose architectural facts could have changed as a result of the file change, going beyond the literal file(s) touched (e.g., a change to `IOrderService.cs` also potentially affects every implementor and every caller-resolved edge).
3. **Rebuilds affected nodes** by invoking the Architecture Scanner's incremental scanning API against a *scoped* set of files/projects rather than the whole solution.
4. **Recalculates dependencies** for the affected subgraph (edges: references, calls, implements, inherits, injects, uses, publishes, consumes, owns, contains).
5. **Updates the Graph Store** using its incremental upsert / delete-by-file primitives, inside a single logical transaction per debounce cycle.
6. **Notifies connected clients** (dashboard browser sessions, MCP server sessions holding a live subscription) over the SignalR hub with a structured change-notification payload.
7. (Phase 3+) **Triggers downstream analysis** — architecture metrics recalculation and circular dependency detection — scoped to the affected subgraph, and (Phase 4) **historical snapshot creation**, **multi-repo fan-out**, and **cloud sync push**.

The watcher is explicitly *not* a scanner reimplementation. It owns file-system observation, change coalescing, blast-radius computation, orchestration, and notification. It delegates all "understand the code" work to the Scanner and all "persist the graph" work to the Graph Store. This keeps the watcher a thin, restartable, stateless-between-runs orchestration layer whose correctness depends on the contracts described in Section 4.

### Non-goals

- The watcher does not parse source code itself (that's the Scanner's job).
- The watcher does not decide graph schema or storage format (that's the Graph Store's job).
- The watcher does not render UI (that's the Dashboard's job) — it only emits events.
- The watcher is not a build system; it does not compile or run the target repository.

---

## 2. Phase-by-Phase Scope

The README lists `arch watch` in the Phase 1 CLI command examples, but lists "Incremental watcher" as a Phase 3 roadmap deliverable. This is intentional and is reconciled as follows:

### Phase 1 — CLI stub only (no incremental behavior)

- `arch watch` exists as a **registered CLI command** so the command surface is stable from day one and scripts/docs can reference it without breaking later.
- Behavior in Phase 1: it prints a message such as `Watch mode is not yet implemented in this version. Run 'arch scan' to refresh the graph.` and exits (or, optionally, loops on a fixed timer calling full `arch scan` every N minutes as a crude polling fallback — see "Phase 1 fallback mode" below). No file-system event subscription, no blast-radius logic, no SignalR notification.
- Rationale: Phase 1 is scanner + storage + CLI + basic MCP. There is no incremental scanning API yet (Scanner Phase 1 only does full scans), so a "real" watcher has nothing correct to call.
- **Phase 1 fallback mode (optional, recommended)**: `arch watch --poll-interval 5m` runs a naive loop that calls full `arch scan` on an interval and diffs the resulting graph snapshot against the previous one purely at the storage layer (via the Graph Store's snapshot/change-log table, see `02-graph-store.md`). This gives early users *some* value from `arch watch` and exercises the SignalR notification path end-to-end before true incrementality exists — but it is O(repo size) per cycle, not O(changed files), and must be clearly logged as such (`[watch] full rescan mode — incremental engine not yet active`).

### Phase 2 — Still not built; unaffected

- Phase 2 work is dashboard, interactive graph, impact analysis, Mermaid export, architecture explorer. The watcher is untouched. `arch watch` continues to behave as in Phase 1 (stub or polling fallback). The only Phase 2-relevant prep work worth doing opportunistically: the SignalR hub and dashboard client are being built in Phase 2 for other reasons (impact analysis pushes, etc.) — the watcher team should coordinate the hub's message contract now so Phase 3 doesn't need a breaking change (see Section 4.3).

### Phase 3 — Full incremental watcher ships

This is the real implementation:

- Native OS file-system event subscription (`FileSystemWatcher` on Windows/.NET, inotify/FSEvents via the same abstraction on Linux/macOS) with a polling-based reconciliation fallback for reliability.
- Debounce/coalescing engine so rapid saves (autosave, atomic rename patterns, `dotnet format`, bulk find/replace) collapse into a single unit of work.
- Blast-radius computation using the Graph Store's existing edges to determine the transitive set of nodes that need re-evaluation, not just the literal changed files.
- Scoped invocation of the Scanner's incremental scan API (changed files/projects in, delta of nodes/edges out).
- Scoped Graph Store update (upsert/delete-by-file, single change-log entry).
- SignalR broadcast of a structured `GraphUpdated` event.
- Triggering of **Architecture Metrics** recalculation and **Circular Dependency Detection**, both scoped to the affected subgraph (both are Phase 3 roadmap items and are designed to plug into the same "affected node set" the watcher already computes).
- Concurrency/locking so a running full `arch scan` and the watcher never race on the Graph Store.

### Phase 4 — Multi-repo, cloud sync, historical snapshots

- **Multi-repository watching**: one watcher process (or supervisor) manages N independent watch sessions, one per configured repository, each with its own debounce/blast-radius state but sharing a connection pool to the Graph Store (or, if the target is a cloud graph store, sharing a client to it).
- **Cloud sync**: incremental deltas are pushed to a remote/cloud graph store (in addition to, or instead of, local SQLite), enabling team collaboration on a shared architecture graph.
- **Historical snapshots**: every watcher-triggered update that materially changes the graph creates a timeline entry (feeds the dashboard's Architecture Timeline view: "+28 classes, +3 projects, -1 interface").

The table below summarizes:

| Capability | Phase 1 | Phase 2 | Phase 3 | Phase 4 |
|---|---|---|---|---|
| `arch watch` CLI command exists | Yes (stub) | Yes (stub) | Yes (real) | Yes (real) |
| File-system event watching | No | No | Yes | Yes |
| Debounce/coalescing | No | No | Yes | Yes |
| Blast-radius computation | No | No | Yes | Yes |
| Scoped incremental scan invocation | No | No | Yes | Yes |
| Scoped graph store update | No (or full-snapshot diff in fallback mode) | No | Yes | Yes |
| SignalR live notification | No (fallback mode may exercise plumbing) | Hub exists for other features | Yes | Yes |
| Metrics recalculation trigger | No | No | Yes | Yes |
| Circular dependency detection trigger | No | No | Yes | Yes |
| Multi-repo concurrent watch | No | No | No | Yes |
| Cloud sync push | No | No | No | Yes |
| Historical snapshot/timeline entries | No | No | Optional (basic change-log) | Yes (full timeline) |

---

## 3. Technical Design

### 3.1 File-system watching mechanism

The watcher uses a **hybrid push + poll reconciliation** model rather than trusting OS file events alone, because:

- `FileSystemWatcher` on .NET/Windows can silently drop events under high volume (internal buffer overflow → `Error` event with `InternalBufferOverflowException`), and behaves differently across network drives, WSL-mounted paths, and Docker bind mounts.
- Editors and tools frequently use **atomic-rename saves** (write to `file.cs.tmp`, then rename over `file.cs`), which surfaces as a `Deleted` + `Created` pair, or on some platforms a single `Renamed` event with `OldFullPath` pointing at the temp file.
- Some CI/dev-container file systems (overlayfs, certain network shares) do not reliably deliver inotify/FSEvents at all.

Design:

```text
┌─────────────────────────────────────────────────────────┐
│                    IFileChangeSource                     │
│  (abstraction — allows swapping implementations/tests)   │
└─────────────────────────────────────────────────────────┘
        │                                   │
        ▼                                   ▼
┌───────────────────────┐     ┌───────────────────────────┐
│ NativeWatcherSource    │     │ PollingReconciliationSource│
│ (FileSystemWatcher /   │     │ (hash/mtime scan every N s)│
│  inotify / FSEvents)   │     │  — safety net + fallback  │
└───────────────────────┘     └───────────────────────────┘
        │                                   │
        └───────────────┬───────────────────┘
                         ▼
              Raw change events (path, kind, ts)
                         ▼
                 Normalization layer
      (dedupe rename-pairs, resolve to canonical path,
       apply ignore rules from config: bin/obj/node_modules)
                         ▼
                  Debounce/Coalescing
```

- `NativeWatcherSource` wraps one `FileSystemWatcher` per watched root (per project directory or per solution root, configurable) with `IncludeSubdirectories = true`, `NotifyFilter = FileName | LastWrite | DirectoryName`, filtering to source-relevant extensions (`*.cs`, `*.csproj`, `*.sln`, `*.json` config files, extensible per language in future phases). On `InternalBufferOverflowException` it logs a warning and immediately triggers a full-reconciliation poll rather than assuming it caught everything.
- `PollingReconciliationSource` runs every `pollIntervalSeconds` (default 30s, configurable, disableable) and does a cheap `mtime` + size scan (falling back to content hash only when mtime/size are ambiguous, e.g. some CI checkouts normalize timestamps) across watched roots, diffing against the last known file manifest kept in memory (and persisted to a small local cache file so a watcher restart doesn't force a full rescan). This is the safety net that guarantees eventual consistency even if native events are lost, and it is the **only** mechanism used when native watching is unavailable or explicitly disabled (`--no-native-watch`, useful in constrained containers).
- Both sources feed the same normalized event stream; the normalization layer is responsible for collapsing `Deleted(file.cs.tmp)+Created(file.cs)` style atomic-rename pairs that occur within a short window (default 2s) into a single `Modified(file.cs)` event, using filename-similarity + proximity-in-time heuristics (same directory, extension match, event gap < threshold).

### 3.2 Debounce / coalescing strategy

Rapid saves (editor autosave, format-on-save, `git checkout` of a branch touching many files, bulk rename refactors) must not trigger one rebuild cycle per file event.

- Each normalized file-change event is added to a **pending change set** (a `HashSet<string>` keyed by canonical repo-relative path, with the change kind: `Added | Modified | Deleted | Renamed`).
- A debounce timer (default **1500ms**, configurable via `watch.debounceMs`) resets on every new incoming event. When the timer elapses with no new events, the pending set is "flushed" into a single **watch cycle**.
- A **max-wait ceiling** (default 10s, `watch.maxDebounceMs`) guarantees a flush happens even under continuous activity (e.g., a long-running `git checkout` or IDE reformat of hundreds of files), so the graph doesn't starve of updates during sustained churn.
- Renames are represented as `Deleted(oldPath) + Added(newPath)` at the blast-radius stage unless the scanner's incremental API supports rename-aware diffing directly (see Section 4.1) — if it does, the watcher passes the rename pair through so the scanner can preserve node identity instead of delete+recreate (important for keeping stable node IDs across renames, which matters for the timeline and for external references like MCP session caches).
- During an active watch cycle (scan + graph update in flight), new file events are **not** dropped — they accumulate into the *next* pending set so they aren't lost, but they do not interrupt or restart the in-flight cycle (see 3.4 concurrency model).

```text
t=0.0s   file A saved        -> pending={A}          timer reset (fires 1.5s)
t=0.3s   file B saved        -> pending={A,B}         timer reset (fires 1.8s)
t=0.4s   file A saved again  -> pending={A,B}          timer reset (fires 1.9s)
t=1.9s   [no new events]     -> FLUSH cycle 1: {A,B}
```

### 3.3 Dependency-aware "blast radius" computation

The literal set of changed files is only the starting point. Architectural correctness requires rebuilding every node whose derived facts could be stale as a result. The blast-radius algorithm:

1. **Direct nodes**: resolve changed file paths to the graph node(s) they own (a `.cs` file typically owns one or more type nodes; a `.csproj` file owns a project node and potentially triggers reference-graph changes).
   - Deleted files: mark their owned nodes for tombstoning.
   - Added files: no existing node; scanner will mint new node IDs.
2. **Reverse-edge expansion (one hop is not enough — traverse until fixpoint or depth cap)**: query the Graph Store for all edges *into* the direct nodes across the relationship types that are "fact-affecting" when the source changes:
   - `Implements` / `Inherits` (a changed interface's method signature affects implementors)
   - `Calls` (a changed method's signature affects callers — though call-graph edges themselves are usually recomputed on the *caller's* file, not requiring a full caller rebuild, unless a public signature changed)
   - `Injects` (DI registration changes can affect every consumer of a changed service registration)
   - `References` at the project level (a changed `.csproj`'s dependency list affects the project's dependents' resolvable symbol set only in edge cases — typically bounded to "recompute this project's outgoing References edges", not neighbors, unless the project itself was added/removed)
3. **Depth cap and containment boundary**: full transitive closure over "Calls" edges could balloon to the entire codebase for a widely-used interface. The watcher applies:
   - A configurable **max blast-radius depth** (default 2 hops for `Calls`/`Uses`, unbounded for `Implements`/`Inherits` since implementor sets are usually small and correctness-critical).
   - A **project-boundary short-circuit**: nodes are grouped by owning project; once an affected project is fully marked "needs rescan," further expansion within that same project is free (it's getting rescanned anyway) — expansion cost is paid only at project *boundaries*.
4. **Scanner scoping**: the final blast-radius output is expressed as two lists handed to the Scanner:
   - `changedFiles`: literal files that changed on disk.
   - `affectedProjects`: the minimal set of projects that must be reloaded into the Roslyn workspace to correctly resolve symbols for the changed files and their direct dependents (Roslyn needs the containing project *and* any project that could have new/changed symbol resolution — see Integration Contract 4.1).

```text
Changed: OrderService.cs (implements IOrderService)
                     │
        ┌────────────┴─────────────┐
        ▼                          ▼
  Direct node:               Reverse edges (Implements):
  OrderService (class)       IOrderService is referenced by
                             OrderController (Injects),
                             OrderServiceTests (references)
                     │
                     ▼
        affectedProjects = { Business, API, Tests }
        changedFiles     = { OrderService.cs }
```

5. **Cycle safety**: because `Calls`/`References` can form cycles, expansion tracks a visited-set and never re-enqueues a node; depth counting is by BFS layer, not by path length, so cycles cannot cause infinite loops or exponential blowup.

### 3.4 Concurrency / locking model

The watcher must never race with:
- A manually invoked `arch scan` (full rescan) running concurrently.
- Two overlapping watch cycles (should be structurally impossible given the debounce design, but defended anyway).
- Phase 4: multiple repository watch sessions writing to a shared cloud graph store.

Design:

- **Single-writer-per-repo lease**: before starting a watch cycle, the watcher acquires a **repo-scoped advisory lock** at the Graph Store level (a row in a `watch_locks` table keyed by repo ID, with an owner token + expiry, renewed via heartbeat — same pattern as a distributed job lock). `arch scan` acquires the same lock class before writing. If the lock is held, the watcher logs `[watch] scan in progress by <owner>, deferring cycle` and retries after a backoff, re-merging any file events that arrived meanwhile into the still-pending set.
- **In-process serialization**: within one watcher process, cycles are processed by a single-threaded orchestration loop (an `System.Threading.Channels`-backed queue of "cycle requests") — the debounce timer callback enqueues a request; the loop dequeues, executes, and only then allows the next flush to be picked up. This guarantees at most one Scanner invocation and one Graph Store transaction in flight per repo at a time from this process.
- **Optimistic concurrency at the Graph Store**: every incremental update carries the `graphVersion`/`changeLogSequence` it was computed against (see `02-graph-store.md`); if the store detects the version has moved (e.g., someone ran `arch scan` manually between blast-radius computation and commit), the update is rejected with a version-conflict error, and the watcher recomputes blast radius against the new baseline before retrying — avoiding silently clobbering a newer graph state with stale delta data.
- **Transaction scope**: the Scanner's incremental result (nodes/edges delta) is applied to the Graph Store as one atomic transaction per watch cycle (see 4.2) so that partial/half-applied graph states are never visible to readers or to the SignalR notification step — notification only fires after commit succeeds.
- **Backpressure**: if watch cycles start queuing up faster than they can be processed (pathological churn, e.g., a huge branch switch), the watcher collapses *queued* cycle requests into one (their pending file sets are unioned) rather than processing them serially — the queue depth for "pending cycles" is effectively capped at 1 beyond the in-flight one.

---

## 4. Integration Contracts

The watcher is a consumer of three interfaces owned by other components. This section specifies exactly what the watcher expects; the owning documents (`01-architecture-scanner.md`, `02-graph-store.md`, `05-rest-api.md`) are the source of truth for the actual implementation, but the watcher's design is only valid if these contracts hold.

### 4.1 Scanner: Incremental Scan API (consumed from `01-architecture-scanner.md`)

Expected shape (C# interface, illustrative):

```csharp
public interface IIncrementalScanner
{
    /// Rescans only the given files/projects and returns a delta.
    /// Must resolve symbols using the full workspace (loads containing
    /// + dependent projects as needed) but only emits nodes/edges that
    /// are new, changed, or removed relative to the current graph.
    Task<ScanDelta> RescanAsync(IncrementalScanRequest request, CancellationToken ct);
}

public sealed record IncrementalScanRequest(
    IReadOnlyList<string> ChangedFiles,      // repo-relative paths, from blast-radius step
    IReadOnlyList<string> AffectedProjects,   // project files to reload into workspace
    IReadOnlyList<FileRename> Renames,        // optional rename-aware pairs
    Guid BaselineGraphVersion                 // version the blast radius was computed against
);

public sealed record ScanDelta(
    IReadOnlyList<GraphNode> UpsertedNodes,
    IReadOnlyList<string> RemovedNodeIds,
    IReadOnlyList<GraphEdge> UpsertedEdges,
    IReadOnlyList<string> RemovedEdgeIds,
    IReadOnlyList<string> FilesScanned,       // full set actually touched (may exceed request due to workspace resolution)
    ScanDiagnostics Diagnostics               // parse errors, unresolved symbols, timing
);
```

Watcher-side expectations:
- `RescanAsync` is safe to call with a small file set without triggering a full-solution reload (this is the entire point of "incremental" — Phase 1/2 scanners that only support full scans are explicitly *not* sufficient, which is why Phase 1's `arch watch` is a stub).
- The scanner may legitimately scan *more* files than requested (e.g., it needs to reload a dependent project to resolve a changed interface) — it reports the true `FilesScanned` set back so the watcher can reconcile its own file-manifest cache (used by the polling reconciliation source) without double-processing those files as "new changes" next cycle.
- The scanner is expected to be **idempotent and side-effect-free on the Graph Store** — it returns a delta; it does not write to storage itself. The watcher owns the write step so it can enforce the locking model in 3.4.
- If `BaselineGraphVersion` is stale by the time the scanner responds, the watcher treats the result as advisory and re-validates against Graph Store's optimistic concurrency check before committing (4.2).

### 4.2 Graph Store: Incremental Update API (consumed from `02-graph-store.md`)

Expected shape:

```csharp
public interface IGraphStoreIncrementalWriter
{
    Task<CommitResult> ApplyDeltaAsync(GraphDelta delta, Guid expectedBaselineVersion, CancellationToken ct);
}

public sealed record GraphDelta(
    IReadOnlyList<GraphNode> UpsertNodes,
    IReadOnlyList<string> DeleteNodeIds,
    IReadOnlyList<GraphEdge> UpsertEdges,
    IReadOnlyList<string> DeleteEdgeIds,
    string TriggeredBy,          // "watcher" | "manual-scan" | "cli"
    IReadOnlyList<string> SourceFiles
);

public sealed record CommitResult(
    bool Success,
    Guid NewGraphVersion,
    long ChangeLogSequence,
    string? ConflictReason        // set when Success = false due to version mismatch
);
```

Watcher-side expectations:
- `ApplyDeltaAsync` is atomic (all-or-nothing) and appends exactly one row to the Graph Store's change-log/snapshot table per successful call, stamped with `TriggeredBy = "watcher"` and the source files, which is what the Phase 4 historical timeline and Phase 1 fallback-mode diffing both read from.
- Delete-by-file: when a watched file is deleted, the watcher passes the owned node IDs (resolved via the blast-radius step, or via a store-side `DeleteNodesByFile(path)` convenience if the Graph Store exposes one) rather than requiring the scanner to "scan" a file that no longer exists.
- Version conflicts (`Success = false`) are retried by the watcher: recompute blast radius against `NewGraphVersion`... actually against current HEAD, then re-run 4.1 and retry the commit, bounded to a small retry count (default 3) before logging an error and requiring manual `arch scan`.
- The watcher does not query the Graph Store's read API for its own purposes beyond blast-radius edge lookups (3.3) and version checks — it is not a general graph client.

### 4.3 REST API / SignalR: Notification Hub (consumed from `05-rest-api.md`)

Expected shape:

```csharp
public interface IArchitectureHubPublisher
{
    Task PublishGraphUpdatedAsync(GraphUpdatedEvent evt, CancellationToken ct);
}
```

Watcher-side expectations:
- The hub is reachable in-process (if the watcher runs as a background service hosted inside the same ASP.NET Core process as the REST API — the default deployment for Phase 3) via direct `IHubContext<ArchitectureHub>` injection, **or** out-of-process (if the watcher runs as a standalone CLI/daemon separate from the API host) via an HTTP callback or a lightweight message broker — the plan defaults to **in-process hosting** for Phase 3 to avoid extra infrastructure, with the standalone-process mode listed as a Phase 4 option needed for multi-repo/cloud scenarios where the watcher may run on a developer machine while the hub lives in the cloud.
- Publishing is fire-and-forget from the perspective of graph correctness (a dropped notification does not corrupt the graph — clients can always fall back to polling `/graph` or reconnect and re-sync), but the watcher logs publish failures and increments a metric so silent notification loss is observable.
- Only one event type is required from the watcher's side for Phase 3: `GraphUpdated` (Section 5). The hub itself may also carry other event types (e.g., impact-analysis push results from Phase 2) — the watcher does not need to know about those.

---

## 5. Change Notification Payload Design

Sent as a SignalR message (hub method e.g. `graphUpdated`) to all connected clients subscribed to a given repo's graph. Also usable as the response body if a client requests the "latest changes since version X" over plain HTTP as a reconnect-catchup fallback.

```json
{
  "eventType": "GraphUpdated",
  "repoId": "confetil-erp-api",
  "graphVersion": "b6a1e9d2-...",
  "previousGraphVersion": "1f0033ab-...",
  "changeLogSequence": 4821,
  "timestamp": "2026-07-24T14:03:11.482Z",
  "triggeredBy": "watcher",
  "cycle": {
    "changedFiles": [
      "src/Modules/Orders/.../OrderService.cs"
    ],
    "affectedProjects": [
      "PatternVision.Modules.Orders.Application",
      "PatternVision.Api"
    ],
    "debounceWindowMs": 1500,
    "durationMs": 812
  },
  "graphDelta": {
    "nodesUpserted": 3,
    "nodesRemoved": 0,
    "edgesUpserted": 7,
    "edgesRemoved": 1,
    "nodeIdsUpserted": ["node:OrderService", "node:IOrderService", "node:OrderController"],
    "nodeIdsRemoved": []
  },
  "metrics": {
    "recalculated": true,
    "scope": "affectedProjects",
    "summary": {
      "couplingDeltaByProject": {
        "PatternVision.Modules.Orders.Application": 0.02
      }
    }
  },
  "circularDependency": {
    "checked": true,
    "scope": "affectedProjects",
    "newCyclesDetected": [],
    "resolvedCycles": []
  },
  "diagnostics": {
    "parseErrors": [],
    "unresolvedSymbols": []
  }
}
```

Design notes:

- **Payload stays a summary, not the full delta blob**, for large changes — actual node/edge bodies are not embedded; clients fetch full node details via the REST `/graph` endpoints keyed by the `nodeIdsUpserted` list, keeping SignalR messages small and avoiding a duplicate serialization surface. A `graphDelta.truncated: true` flag plus a `deltaFetchUrl` is included if the ID list itself would be too large (configurable cap, default 200 IDs), pointing clients at `GET /graph/delta?since={previousGraphVersion}`.
- **`previousGraphVersion` + `graphVersion`** let clients that missed a message (reconnect scenario) detect the gap and call the delta-catchup endpoint instead of silently drifting.
- **`triggeredBy`** distinguishes watcher-originated updates from a manual `arch scan`, since the dashboard may want to render them differently (e.g., a live pulse animation only for watcher events).
- **`metrics` and `circularDependency` blocks** are present but empty/omitted-fields in cycles where Phase 3 hooks are disabled or not yet built (see Section 6); their presence is versioned so Phase 2-era clients (built before these existed) can ignore unknown fields safely (payload is designed additive-only, never renaming/removing fields across phases).
- **Timestamps are UTC ISO-8601** with millisecond precision, generated by the watcher process at commit time (not at file-change-detection time) — see Section 10 for clock-skew discussion re: the Phase 4 timeline.

---

## 6. Metrics & Circular Dependency Detection Hooks (Phase 3)

Both "Architecture metrics" and "Circular dependency detection" are listed as independent Phase 3 roadmap items, but they are designed to be **triggered by the watcher** as a post-commit step, scoped to the same affected-project set computed in 3.3 — avoiding a second, unrelated blast-radius computation.

```text
Watch cycle commits GraphDelta
              │
              ▼
   ┌──────────────────────────┐
   │ IArchitectureMetricsEngine│  (owned by its own component/plan,
   │ .RecalculateAsync(        │   invoked here as a hook)
   │    affectedProjects)      │
   └──────────────────────────┘
              │
              ▼
   ┌──────────────────────────┐
   │ ICircularDependencyChecker│
   │ .CheckAsync(              │
   │    affectedProjects)      │
   └──────────────────────────┘
              │
              ▼
     Results folded into GraphUpdatedEvent
     (metrics + circularDependency blocks)
              │
              ▼
        SignalR publish (Section 5)
```

- **Scoping rule**: metrics recalculation runs over the affected projects **plus their direct neighbors** (one hop), since coupling metrics are inherently relational (e.g., "afferent coupling" of project X depends on who references X, which may not itself be in the affected set if only X's internals changed but its public surface didn't). The watcher passes `affectedProjects ∪ neighbors(affectedProjects)` to the metrics engine, not the raw scanner scope, to keep this correct without forcing the metrics engine to redo its own reverse-edge lookup.
- **Circular dependency scoping**: cycle detection must run over the **project reference graph containing** the affected projects, not just the affected set in isolation (a cycle can only be introduced or removed by looking at the full loop, which may pass through unaffected projects). The watcher requests cycle detection over the *connected component* (in the project-reference graph) that contains any affected project — bounded, since most repos have a handful of connected components in their project graph, not one giant blob (this should be validated against real repos during Phase 3 testing; if it turns out one giant connected component is typical, the circular-dependency component may need its own incremental cycle-detection algorithm rather than full recomputation per cycle — flagged as an open question in Section 10).
- **Failure isolation**: if metrics recalculation or cycle detection throws, the watcher still commits the graph delta and still publishes `GraphUpdated` — it sets `metrics.recalculated: false` / `circularDependency.checked: false` with an `error` field, rather than rolling back a structurally-valid graph update because an analysis add-on failed. Graph correctness must not depend on these optional hooks succeeding.
- **Debounce interaction**: metrics/cycle-detection hooks run once per watch cycle (post-commit), not once per file — this is the main reason they are described as "hooks off the watcher" rather than "hooks off the scanner," since the scanner may be invoked in ways that don't map 1:1 to a graph commit (e.g., retries).
- **Config**: `watch.hooks.metrics.enabled` and `watch.hooks.circularDependency.enabled` (both default `true` in Phase 3+) allow disabling either hook for performance-sensitive large-repo scenarios without disabling the watcher itself.

---

## 7. Multi-repo & Cloud Sync Design (Phase 4)

### 7.1 Multi-repository watching

- A **watch supervisor** process manages N `RepoWatchSession` instances, one per configured repository (from a `watch.repos: [...]` config list, or auto-discovered via a workspace file).
- Each `RepoWatchSession` owns its own: file-change sources (3.1), debounce state (3.2), blast-radius cache (3.3), and repo-scoped lock (3.4). Sessions do not share in-memory state, but they **do share**:
  - A connection pool to the Graph Store (or cloud graph store).
  - A single SignalR hub connection (multiplexed; clients subscribe per-`repoId` group so a dashboard watching repo A doesn't receive repo B's noise).
- Resource governance: the supervisor caps total concurrent scanner invocations across all sessions (`watch.maxConcurrentScans`, default = number of CPU cores / 2) since Roslyn workspace loads are memory/CPU heavy; sessions queue behind this global semaphore rather than each independently maxing out the machine.
- Cross-repo edges (e.g., a shared NuGet package consumed by two watched repos, or a service-to-service call captured via OpenAPI/contract scanning in a future phase) are out of scope for Phase 4's initial multi-repo support — each repo's graph remains logically separate with an optional `repoId` namespace prefix on node IDs; true cross-repo graph merging is a later concern (flagged as an open question, not committed here).

### 7.2 Cloud sync

- The local watcher continues to write to a local Graph Store (SQLite, per the README's phased storage plan) as the fast, low-latency source of truth for the local CLI/MCP/dashboard.
- After each successful local commit, the watcher (or a decoupled `CloudSyncWorker` that tails the local change-log table rather than being wired directly into the watch-cycle critical path) pushes the same `GraphDelta` to a remote/cloud graph store endpoint (`POST /cloud/graph/delta`), authenticated per the platform's (future) Better Auth / OAuth setup.
- **Design choice: decouple cloud push from the watch cycle's hot path.** The watcher's local commit + notification must not block on network I/O to a cloud service. A durable local outbox (a `pending_cloud_sync` table, append-only, drained by a background worker with retry/backoff) is used so cloud sync is eventually-consistent and resilient to connectivity loss — this mirrors the outbox pattern already implicit in the change-log table.
- Conflict handling for cloud sync (two team members' local watchers both pushing deltas for overlapping subgraphs) is delegated to the Graph Store's cloud-side merge policy (owned by `02-graph-store.md` and, likely, a "Team collaboration" plan doc not yet written) — the watcher's only obligation is to push deltas tagged with enough provenance (`repoId`, `machineId`/`userId`, `localGraphVersion`) for the cloud side to reconcile.

### 7.3 Historical snapshots / timeline

- Every successful `ApplyDeltaAsync` commit already produces a change-log row (4.2). Phase 4's timeline feature is primarily a **read-side aggregation** over that change-log (rollups per day: "+28 classes, +3 projects, -1 interface"), so the watcher's Phase 4 responsibility is narrow:
  - Ensure every commit's change-log row carries enough shape info (counts of node/edge types added/removed, not just IDs) that daily rollups can be computed without re-diffing full snapshots.
  - Optionally materialize a periodic **full snapshot** (not just incremental deltas) every configurable interval (default: daily, or every N commits) purely as a fast-recovery/rebuild-from-snapshot mechanism and as a defensive measure against change-log drift over very long-running graphs — this is a Graph Store concern primarily, but the watcher is the natural trigger point (`watch.snapshot.intervalCommits`).
- Timeline entries use **commit time at the Graph Store**, not file-modified-time or event-detection time, as the canonical timestamp, specifically to sidestep the clock-skew problem discussed in Section 10.

---

## 8. Project / Module Structure

Proposed layout (C#/.NET, matching the Scanner's tech stack and the platform's overall .NET-centric backend):

```text
src/
  Arch.Watcher/                          # class library — core watcher engine
    IFileChangeSource.cs
    NativeWatcherSource.cs
    PollingReconciliationSource.cs
    ChangeNormalizer.cs                  # atomic-rename collapsing, ignore rules
    DebounceCoalescer.cs
    BlastRadiusCalculator.cs
    WatchCycleOrchestrator.cs            # ties everything together, owns the lock
    RepoWatchSession.cs                  # Phase 4: one session per repo
    WatchSupervisor.cs                   # Phase 4: manages multiple sessions
    Contracts/
      IIncrementalScanner.cs             # interface only — impl lives in Scanner project
      IGraphStoreIncrementalWriter.cs    # interface only — impl lives in Graph Store project
      IArchitectureHubPublisher.cs       # interface only — impl lives in REST API project
      GraphUpdatedEvent.cs
    Configuration/
      WatchOptions.cs                    # debounceMs, maxDebounceMs, pollIntervalSeconds,
                                          # maxBlastRadiusDepth, hooks.*, repos[], snapshot.*
    Hooks/
      IMetricsRecalculationHook.cs
      ICircularDependencyHook.cs
    CloudSync/                            # Phase 4
      CloudSyncWorker.cs
      PendingCloudSyncOutbox.cs

  Arch.Cli/
    Commands/
      WatchCommand.cs                     # `arch watch` — Phase 1: stub/poll fallback;
                                           # Phase 3+: hosts Arch.Watcher engine
                                           # Phase 4: hosts WatchSupervisor for multi-repo

  Arch.Api/                               # existing REST API / SignalR host project
    Hubs/
      ArchitectureHub.cs                  # publishes GraphUpdated (owned by 05-rest-api.md)
    HostedServices/
      WatcherHostedService.cs             # BackgroundService wrapper that runs
                                           # Arch.Watcher in-process with the API (Phase 3 default)

tests/
  Arch.Watcher.Tests/
    ChangeNormalizerTests.cs
    DebounceCoalescerTests.cs
    BlastRadiusCalculatorTests.cs
    WatchCycleOrchestratorTests.cs
    ConcurrencyLockingTests.cs
    Fakes/
      FakeIncrementalScanner.cs
      FakeGraphStoreWriter.cs
      FakeHubPublisher.cs
  Arch.Watcher.IntegrationTests/
    FileSystemSimulationTests.cs          # real temp-dir + real FileSystemWatcher
    EndToEndWatchCycleTests.cs            # real Scanner + real (SQLite) Graph Store
```

- **Deployment modes**:
  - *In-process* (Phase 3 default): `WatcherHostedService : BackgroundService` runs inside the same ASP.NET Core host as the REST API, so `IArchitectureHubPublisher` is just a thin wrapper over `IHubContext<ArchitectureHub>` — no network hop for notifications.
  - *Standalone* (Phase 4 option, needed when the watcher runs on a developer's machine against a cloud-hosted API/hub): `arch watch` runs as a long-lived CLI process (or OS service/daemon — Windows Service, systemd unit, launchd agent) and talks to the hub via a SignalR client connection instead of `IHubContext`.
- `WatchOptions` binds from the existing YAML config (the README's example config file) under a new `watch:` section, e.g.:

```yaml
watch:
  debounceMs: 1500
  maxDebounceMs: 10000
  pollIntervalSeconds: 30
  nativeWatch: true
  maxBlastRadiusDepth: 2
  hooks:
    metrics:
      enabled: true
    circularDependency:
      enabled: true
  snapshot:
    intervalCommits: 200
  repos:               # Phase 4 only
    - id: confetil-erp-api
      path: C:\Users\Daniel\Desktop\Projects\Confetil\Confetil.ERP.API
    - id: patternvision-frontend
      path: C:\Users\Daniel\Desktop\Projects\Confetil\patternvision-frontend
  cloudSync:            # Phase 4 only
    enabled: false
    endpoint: https://cloud.arch-intel.example/api
```

---

## 9. Testing Strategy

### 9.1 Unit tests (no real file system, no real Scanner/Graph Store — fakes throughout)

- **`ChangeNormalizerTests`**: given a synthetic stream of raw events including a `Deleted(x.cs.tmp)` immediately followed by `Created(x.cs)` within the collapse window, assert a single `Modified(x.cs)` is emitted; assert the same pair *outside* the window emits two separate events; assert ignored paths (`bin/`, `obj/`, `node_modules/`) never reach the debounce stage.
- **`DebounceCoalescerTests`**: simulate rapid synthetic events (using a virtualized/fake clock, not real `Task.Delay`, so tests run instantly) confirming: (a) the timer resets on each new event, (b) a single flush occurs after quiescence, (c) the `maxDebounceMs` ceiling forces a flush under continuous simulated activity, (d) the pending set correctly unions multiple changes to the same file into the latest change kind.
- **`BlastRadiusCalculatorTests`**: build a small in-memory fake graph (nodes/edges) representing a known topology (interface + 2 implementors + 1 caller + unrelated node), assert: (a) the correct affected node/project set is returned for a change to the interface file, (b) depth-cap correctly excludes nodes beyond `maxBlastRadiusDepth` hops on `Calls` edges, (c) `Implements`/`Inherits` expansion is not depth-capped, (d) a cyclic graph does not cause infinite loop / duplicate visits (visited-set assertion), (e) project-boundary short-circuiting avoids redundant expansion.
- **`WatchCycleOrchestratorTests`** (using `FakeIncrementalScanner`, `FakeGraphStoreWriter`, `FakeHubPublisher`): assert a full cycle calls the scanner with exactly the expected scoped request, applies the resulting delta to the store, and publishes a `GraphUpdated` event with a payload matching Section 5's schema — including the "no-op" case (scanner returns an empty delta because the change didn't actually affect any architectural fact) resulting in **no** publish (avoid notification spam for irrelevant file touches, e.g., whitespace-only edits that the scanner determines produce zero delta).
- **`ConcurrencyLockingTests`**: simulate a held lock (fake lock provider returns "held by manual-scan") and assert the watcher defers and retries rather than proceeding; simulate a version-conflict response from `FakeGraphStoreWriter` and assert the retry-with-recompute path is exercised up to the configured retry cap, then fails loudly.

### 9.2 Integration tests (real file system, temp directories, real timers)

- **`FileSystemSimulationTests`**: create a temp directory tree mimicking a small solution; use the real `NativeWatcherSource` (real `FileSystemWatcher`); perform actual rapid saves (write+rename in a tight loop, mimicking common editor save patterns from VS Code/Rider/Visual Studio) and assert the *normalized* event stream collapses correctly and no events are silently lost over, say, 200 rapid saves within a few seconds. Include a scenario that forces an `InternalBufferOverflowException` (flood many events quickly) and assert the polling reconciliation source recovers full correctness afterward.
- **`EndToEndWatchCycleTests`**: run against a real (small, fixture) .NET solution, a real Scanner incremental-scan implementation, and a real SQLite-backed Graph Store (per `02-graph-store.md`). Modify a source file (e.g., add a method to a service), let the real debounce timer elapse (short interval configured for tests), and assert: (a) exactly the expected minimal set of files is passed to the scanner, (b) the graph store reflects the new method/node, (c) unrelated nodes are untouched (verifying "only affected nodes rescanned" — assert via a scan-invocation-count or file-count expectation, not just end-state), (d) a `GraphUpdated` notification is captured by a test SignalR client with the expected shape, (e) metrics/circular-dependency hook results are present per Section 6.
- **Locking/race test**: kick off a manual full `arch scan` and a file-triggered watch cycle concurrently against the same fixture repo; assert no data corruption (no partially-applied delta visible mid-transaction) and no deadlock, using the real advisory-lock table.
- **Load/perf smoke test** (not a strict pass/fail gate, but tracked): time a watch cycle against a fixture repo sized similarly to a real target repo (e.g., a copy of `Confetil.ERP.API`'s scale) for a single-file change, asserting the cycle completes well under the debounce window's "feels instant" budget (target: sub-2s for a small blast radius on a mid-size repo) — flagged as an open performance question in Section 10 for very large repos.

### 9.3 Notification payload contract tests

- Golden-file/schema tests asserting every field in Section 5's JSON shape is present with correct types across representative scenarios (single file change, multi-file batched change, delete-only change, no-op/empty-delta change, metrics-hook-failure scenario, circular-dependency-detected scenario) — these tests double as the contract the dashboard/MCP client teams build against, and should be kept in sync if the payload shape changes (additive-only versioning, per Section 5).

---

## 10. Risks & Open Questions

- **Large-repo watch performance**: blast-radius computation and Roslyn workspace reloads scale with project count and reference-graph density. For very large solutions (hundreds of projects), even a "scoped" incremental scan may need to reload a large chunk of the workspace if the changed file sits near a heavily-depended-upon core project (e.g., a shared `Common`/`Domain` project referenced by everything). Mitigation ideas to validate during Phase 3 implementation: workspace caching/warm-hosting (keep the MSBuild workspace loaded between cycles instead of reloading from cold), and/or a "hot core" designation in config for projects known to be widely referenced, adjusting expansion strategy for them. This is flagged as **not fully solved by this design** and should be load-tested early against a real large repo (e.g., `Confetil.ERP.API`) before Phase 3 is considered done.
- **Editor atomic-rename / double-event patterns**: covered functionally in 3.1/3.2, but different editors (VS Code, Rider, Visual Studio, `dotnet format`, `git` operations) have subtly different save patterns, and some (certain network-mounted drives, WSL2 interop) may not emit rename events cleanly at all. The collapse-window heuristic (2s, same-directory, extension match) is a best-effort heuristic, not a guarantee — worth instrumenting in production (log when a collapse *doesn't* happen for a plausible rename pair) to tune the heuristic over time rather than assuming the initial constants are correct.
- **Clock skew for the historical timeline**: Section 7.3 pins timeline timestamps to Graph Store commit time specifically to avoid trusting client/file-system clocks, but the watcher process's own clock could still drift relative to a cloud graph store's clock in the Phase 4 multi-machine scenario. Recommendation: cloud sync pushes should be timestamped **server-side** at the cloud store (not trusting the pushed `timestamp` field from the local watcher) for anything that feeds a cross-team timeline; the local watcher's own timestamp remains authoritative only for the local single-machine timeline. This needs explicit agreement with whoever owns the cloud graph store's design (Phase 4, not yet a written plan).
- **Circular dependency detection scope blowup**: Section 6 assumes project-reference graphs decompose into multiple connected components in typical repos; if real-world repos (verified against `Confetil.ERP.API`) turn out to have one dominant connected component (common in layered architectures where `Common`/`Domain` is referenced everywhere), "recompute cycles for the connected component containing the affected set" degenerates to "recompute cycles for almost the whole repo" on every change — need to validate this assumption early and consider an incremental cycle-detection algorithm (e.g., maintaining cycle-detection state incrementally rather than recomputing from scratch) if it doesn't hold.
- **False-negative risk in blast-radius depth capping**: capping `Calls`/`Uses` expansion at 2 hops is a deliberate performance/correctness tradeoff; it means some deeply transitive effects (e.g., a changed return type propagating through 3+ layers of wrapper calls before hitting a serialization boundary) could be missed by the incremental path and only caught by a subsequent full `arch scan`. This should be explicitly documented as a known limitation, with `arch scan` recommended periodically (or the Phase 1 polling-fallback pattern reused as a periodic full-reconciliation safety net even in Phase 3+, e.g., a nightly full scan regardless of watch activity) to bound the staleness window.
- **Multi-repo resource contention (Phase 4)**: the global concurrent-scan semaphore (7.1) prevents machine overload but means a change in a low-priority repo could wait behind a slow scan in another — worth considering a priority/fairness scheme (e.g., round-robin or priority-by-recently-active-repo) rather than strict FIFO if this becomes a real usability problem.
- **Standalone-process notification path (Phase 4)**: the standalone deployment mode's SignalR *client* connection (as opposed to in-process `IHubContext`) introduces its own reconnect/backoff/auth concerns that aren't fully designed here — deferred to when Phase 4 standalone mode is actually built, cross-referenced against whatever auth model (`05-rest-api.md` / Better Auth) exists by then.
- **Ignore-rule consistency**: the watcher's ignore rules (bin/obj/node_modules, per the README's example config) must stay in sync with the Scanner's own ignore rules (`01-architecture-scanner.md`) and the Graph Store's expectations about what constitutes a "file" worth a node — a drift between the two (e.g., watcher ignores a path the scanner would still care about) would cause silent staleness. Recommendation: the ignore-rule list should be defined once (shared config section) and consumed by both components rather than duplicated.

---

## 11. Task Breakdown (checklist per phase)

### Phase 1 — CLI stub

- [ ] Register `arch watch` command in the CLI command table.
- [ ] Implement stub behavior: print "not yet implemented" message and exit.
- [ ] (Optional, recommended) Implement polling-fallback mode: `arch watch --poll-interval <duration>` looping full `arch scan` + diff via Graph Store snapshot/change-log table.
- [ ] Document the Phase 1 vs Phase 3 distinction in CLI `--help` text for `arch watch` so users aren't surprised.
- [ ] Wire fallback mode's diff results into the SignalR hub (if the hub already exists by the time this lands) purely to validate the notification plumbing end-to-end early.

### Phase 2 — No watcher work; coordination only

- [ ] Review the SignalR hub's message contract being designed for Phase 2 dashboard features; ensure it can additively accommodate the `GraphUpdated` event shape from Section 5 without a breaking change later.
- [ ] Confirm Graph Store's change-log/snapshot table schema (being built for other Phase 2 reasons, e.g., impact analysis history) is sufficient for the watcher's later needs (`triggeredBy`, `sourceFiles`, per-type add/remove counts).

### Phase 3 — Full incremental watcher

- [ ] Implement `IFileChangeSource` abstraction + `NativeWatcherSource` (FileSystemWatcher-based, Windows-first, cross-platform via .NET's abstraction).
- [ ] Implement `PollingReconciliationSource` as safety-net fallback.
- [ ] Implement `ChangeNormalizer` (ignore rules, atomic-rename collapsing).
- [ ] Implement `DebounceCoalescer` with configurable `debounceMs`/`maxDebounceMs`, using a virtualized clock for testability.
- [ ] Implement `BlastRadiusCalculator` (reverse-edge expansion, depth cap, project-boundary short-circuit, cycle-safe BFS).
- [ ] Define and stabilize `IIncrementalScanner`, `IGraphStoreIncrementalWriter`, `IArchitectureHubPublisher` contracts (coordinate with Scanner/Graph Store/REST API teams).
- [ ] Implement `WatchCycleOrchestrator` tying normalization → debounce → blast radius → scanner call → store commit → hooks → notification.
- [ ] Implement repo-scoped advisory locking (`watch_locks` table or equivalent) shared with `arch scan`.
- [ ] Implement optimistic-concurrency retry loop against Graph Store version conflicts.
- [ ] Implement `WatcherHostedService` for in-process hosting inside the REST API host.
- [ ] Implement Metrics recalculation hook integration (scoped to affected + neighbor projects).
- [ ] Implement Circular Dependency detection hook integration (scoped to connected component containing affected projects).
- [ ] Finalize and version the `GraphUpdated` payload schema (Section 5); publish it as a shared contract for dashboard/MCP consumers.
- [ ] Full unit test suite (Section 9.1).
- [ ] Integration test suite including real-file-system simulation and end-to-end cycle tests (Section 9.2).
- [ ] Notification payload contract/golden tests (Section 9.3).
- [ ] Load/perf smoke test against a realistically large fixture repo; capture baseline numbers and flag if the large-repo risk (Section 10) needs mitigation before ship.
- [ ] `arch watch --help` and docs updated to describe real incremental behavior, config options, and known limitations (blast-radius depth cap, recommendation to periodically run full `arch scan`).

### Phase 4 — Multi-repo, cloud sync, historical snapshots

- [ ] Implement `RepoWatchSession` (per-repo isolated state) and `WatchSupervisor` (manages N sessions, global concurrency semaphore for scanner invocations).
- [ ] Support `watch.repos[]` config for multi-repo definitions; support SignalR hub group-per-`repoId` so clients subscribe selectively.
- [ ] Implement standalone-process deployment mode (SignalR client connection instead of in-process `IHubContext`), including reconnect/backoff.
- [ ] Implement `PendingCloudSyncOutbox` (durable local outbox table) + `CloudSyncWorker` background drain-with-retry process.
- [ ] Define cloud delta-push API contract (`POST /cloud/graph/delta`) and provenance fields (`repoId`, `userId`/`machineId`, `localGraphVersion`) in coordination with the cloud graph store owner.
- [ ] Ensure every local commit's change-log row carries sufficient rollup-friendly shape data (add/remove counts by node/edge type) for the timeline read-side.
- [ ] Implement periodic full-snapshot materialization trigger (`watch.snapshot.intervalCommits`) as a defensive recovery/rebuild mechanism.
- [ ] Validate/resolve the clock-skew question (Section 10): confirm cloud-side server timestamps are authoritative for cross-machine timeline entries.
- [ ] Multi-repo integration tests: concurrent watch sessions across 2+ fixture repos, verifying resource governance (global scan semaphore) and independent correctness per repo.
- [ ] Cloud sync integration tests against a stub/mock cloud endpoint: outbox durability across watcher restarts, retry/backoff behavior, conflict-response handling.
- [ ] Update docs/config reference for `watch.repos`, `watch.cloudSync`, and `watch.snapshot` sections.
