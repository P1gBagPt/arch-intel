# Implementation Plan: Architecture Scanner

Component 1 of the Architecture Intelligence Platform (see root `README.md` for
overall vision, roadmap, and system architecture).

This document is the authoritative engineering plan for the Scanner across all
four roadmap phases. It is written to be cross-referenced by the team building
`02-graph-store.md` (the Graph Store / persistence component) and by whoever
writes the CLI and Configuration plans, since the Scanner owns the config
schema and the graph output contract that those components consume.

---

## 1. Overview & Responsibilities

The Architecture Scanner is the component that turns a `.NET` solution on
disk into a structured, queryable architectural model. It is the only
component in the platform that reads source code directly; everything
downstream (Graph Store, MCP Server, REST API, Dashboard) consumes the model
the Scanner produces, never the raw repository.

### Core responsibilities

1. **Load** a solution via MSBuild Workspace, honoring project load order and
   ignore rules from the scanner's YAML configuration.
2. **Parse** every project's compilation using the Roslyn semantic model
   (never regex/syntax-only parsing) to get accurate, resolved symbol
   information.
3. **Discover** architectural entities: projects, assemblies, namespaces,
   types (classes/interfaces/records/structs/enums), members
   (methods/constructors/properties), and framework-specific concepts
   (controllers, minimal API endpoints, MediatR handlers, domain events, EF
   entities, repositories, services, background workers, hosted services,
   message queue producers/consumers, configuration bindings, tests, DI
   registrations).
4. **Resolve relationships** between those entities (References, Calls,
   Implements, Inherits, Injects, Uses, Publishes, Consumes, Owns, Contains)
   using semantic symbol resolution, not string matching, wherever Roslyn
   makes that possible.
5. **Emit** the resulting node/edge graph through a stable, versioned output
   contract (`ArchScanner.Contracts`) so it can be persisted by the Graph
   Store component without the Scanner knowing anything about SQLite,
   Postgres, or Neo4j.
6. **Scale down to incremental scans** (Phase 3) so that a single changed
   file does not require re-scanning the entire solution.
7. **Stay extensible to non-C# languages** (Phase 4) without a rewrite, by
   isolating all C#/Roslyn-specific logic behind a language-scanner
   abstraction.

### Explicit non-responsibilities

- The Scanner does **not** design or own the graph storage schema (SQLite
  tables, Postgres schema, Neo4j graph model) — that is `02-graph-store.md`'s
  job. The Scanner only owns the DTOs and the writer interface described in
  [Section 4](#4-output-contract).
- The Scanner does **not** serve the REST API, MCP Server, or dashboard. It
  is a producer, not a query engine.
- The Scanner does **not** compute the final "architecture quality score"
  (Phase 4) — it supplies raw metric inputs that a separate scoring engine
  consumes.
- The Scanner does **not** implement file-system watching itself — that is
  the Incremental Watcher component's job — but it exposes the incremental
  scan API that the watcher calls into (see [Section 6](#6-incremental-scanning-design-phase-3)).

---

## 2. Phase-by-Phase Scope

### Phase 1 — Solution Scanner (Foundation)

Ships:

- Full-solution scan via MSBuild Workspace + Roslyn semantic model.
- YAML config loading (`scanOrder`, `ignore`, `languages`, `rules`).
- Discovery of all entity types listed in the README (Projects → Tests).
- Two-pass relationship resolution producing all ten README relationship
  types, at least at "best effort" fidelity for the harder heuristics
  (DI, MediatR, EF, message queues).
- Output via `IGraphWriter` contract, with a first concrete implementation
  (`SqliteGraphWriter`, likely co-owned/handed off to the Graph Store repo)
  and a file-based `NdjsonGraphWriter` for out-of-process / debugging use.
- CLI entry point: `arch scan` (implemented here even though the broader CLI
  plan is a separate document; the Scanner ships the command handler as a
  library that a thin CLI host wires up).
- Deterministic node/edge IDs (content-hash based) — a hard requirement,
  not an optimization, because Phase 2 diffing and Phase 3 incrementality
  both depend on IDs being stable across runs.

Explicitly out of scope for Phase 1: incremental scanning, metrics/coupling
computation, multi-language support. These are stubbed as extension points
only (see Sections 6 and 7) so Phase 1 code doesn't have to be reworked later.

### Phase 2 — Dashboard-Facing Metadata (no new scanning capability)

The Scanner ships **no new scanning logic** in Phase 2. The roadmap's Phase 2
items (Next.js dashboard, interactive dependency graph, impact analysis,
Mermaid export, architecture explorer) are consumed entirely from data the
Phase 1 Scanner already produces, persisted by the Graph Store. The Scanner's
Phase 2 work is:

- Confirm the `ArchNode`/`ArchEdge` contract carries every field the
  dashboard's three initial views need:
  - **Repository Explorer**: needs the `Contains` edge hierarchy
    (Solution → Project → Namespace → Class → Member) and `ProjectName`/
    `FilePath` on every node so the explorer can group by physical layer
    (Business/Infrastructure/API/Tests as shown in the README example).
  - **Dependency Graph view**: needs `References`, `Calls`, `Implements`,
    `Inherits`, `Injects`, `Uses` edges with resolvable source/target node
    IDs and a `Weight`/count so the UI can render edge thickness.
  - **Mermaid export**: needs human-readable `Name` and `Type` on every node
    (Mermaid diagrams are label-based, not ID-based) — add a
    `MermaidSafeId` helper (see Section 4) since Mermaid node IDs can't
    contain arbitrary characters.
- Add a small `IGraphExportFormatter` extension point (`ToMermaid()`) in the
  Scanner's contracts library so both the CLI (`arch diagram Business`) and
  the REST API can share one Mermaid-generation implementation instead of
  duplicating it.
- No schema changes to the writer contract; only additive metadata (e.g.
  making sure `Properties` carries HTTP route templates for endpoints, so
  the dashboard's Service Explorer can display them without re-parsing
  source).

### Phase 3 — Incremental Scanning, Metrics, Circular Dependency Detection

Ships:

- `IIncrementalScanner` API: scan only a given set of changed files/projects
  and recompute only the affected subgraph (full design in
  [Section 6](#6-incremental-scanning-design-phase-3)).
- A persisted **symbol index cache** so incremental runs don't require
  reloading the whole solution's compilations.
- Architecture metrics extraction hooks: afferent/efferent coupling per
  project and per namespace, cyclomatic complexity per method, and a
  circular-dependency detector operating over `References`/`Uses` edges at
  project and namespace granularity (Tarjan's SCC algorithm).
- These metrics are emitted as a new DTO (`MetricSnapshot`, see Section 4)
  written through the same `IGraphWriter`, not bolted onto `ArchNode`.
- Support for the Incremental Watcher component (planned in a separate
  document, referred to here as the Incremental Watcher) to call
  `IIncrementalScanner.ScanChangedAsync(ScanDelta)` after detecting file
  system changes.

### Phase 4 — Multi-Language Groundwork, Quality Scoring Inputs

Ships:

- `ILanguageScanner` abstraction extracted from the existing C# pipeline,
  with `CSharpLanguageScanner` becoming the first (and for now, only)
  concrete implementation of it — no behavior change, pure refactor plus a
  registry/dispatch layer.
- A defined (but not necessarily fully implemented) out-of-process plugin
  protocol so a future TypeScript/Python/Java scanner does not need to run
  inside the .NET host (full design in
  [Section 7](#7-multi-language-extensibility-design-phase-4)).
- Additional raw metric emission needed as **inputs** to the platform's
  Phase 4 "architecture quality scoring" (test coverage proxy via test →
  production-code edge density, layering-violation counts, cycle counts) —
  the Scanner does not compute the composite score itself.

---

## 3. Technical Design

### 3.1 Loading the solution

- Bootstrap with `Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults()`
  before any `Microsoft.CodeAnalysis.MSBuild` type is touched (must run once
  per process, before the workspace is constructed — a common source of
  runtime failures if skipped or ordered wrong).
- Open the solution via
  `MSBuildWorkspace.Create().OpenSolutionAsync(config.SolutionPath)`.
- Subscribe to `workspace.WorkspaceFailed` and record diagnostics rather
  than throwing — a single project failing to load (e.g. missing SDK,
  broken restore) should degrade gracefully and be reported by
  `arch doctor`, not abort the whole scan.
- Apply `ignore` patterns from config at the **project and document** level
  before compilation: skip projects whose path matches an ignore glob
  (`bin`, `obj`, `node_modules` from the README example, plus
  user-specified globs), and skip generated files
  (`*.g.cs`, `*.designer.cs`, anything under `obj/`, anything Roslyn's
  `GeneratedCodeAnalysisFlags` marks as generated).

### 3.2 Respecting `scanOrder`

`scanOrder` in the YAML config is a list of logical layer names (e.g.
`Common, Domain, Application, Infrastructure, API, Tests`). The Scanner:

1. Loads the full MSBuild project graph first (`solution.Projects`), which
   already encodes ground-truth project-reference dependencies.
2. Buckets each project into a `scanOrder` position by matching the layer
   name against a configurable substring/prefix rule on the project name
   (e.g. `PatternVision.Modules.Users.Application` matches `Application`).
   Projects that don't match any `scanOrder` entry are appended at the end,
   in the order MSBuild reports them, with a warning surfaced through
   `arch doctor`.
3. Produces a final ordering that is stable and **used only for**:
   - deterministic output ordering (important for golden-file testing and
     human-readable diffs between scans), and
   - the order in which discovery-pass diagnostics are printed to the
     console.
4. `scanOrder` is explicitly **not** used to gate compilation — see below.

**Compilation itself is not required to be sequential.** Roslyn's
`Project.GetCompilationAsync()` per project can run concurrently
(`Task.WhenAll` over projects, bounded by a configurable degree of
parallelism, default = `Environment.ProcessorCount`) because MSBuildWorkspace
computes each project's compilation independently once documents are loaded;
project-reference compilations are provided as `CompilationReference`s by the
workspace itself. Sequential `scanOrder`-driven compilation is offered as a
`--sequential` debug flag for reproducing issues, but the default path is
parallel for throughput on large solutions.

### 3.3 Two-pass symbol discovery and resolution

This is the most important design decision in the Scanner, and the one most
likely to be gotten wrong if skipped: **Roslyn does not give you one global
compilation for a solution** — each project has its own `Compilation`, and a
type declared in `Domain` is represented as a *different* `INamedTypeSymbol`
instance (though semantically equal) in `Domain`'s own compilation vs. as
seen through a `CompilationReference` from `Infrastructure`'s compilation.
Symbol reference-equality (`SymbolEqualityComparer.Default`) works fine
**within** a single compilation and across direct metadata references, but
the Scanner needs a solution-wide identity scheme that is stable regardless
of which project's compilation produced the symbol. The design:

**Pass 1 — Discovery.** Walk every project (in parallel, order from 3.2 only
for output ordering), and for every project:

- Get the `Compilation` and, for every `SyntaxTree`/`Document`, get the
  `SemanticModel`.
- Run a `CSharpSyntaxWalker` (`ArchDeclarationWalker`) over each tree,
  calling `semanticModel.GetDeclaredSymbol(node)` for every
  `ClassDeclarationSyntax`, `InterfaceDeclarationSyntax`,
  `RecordDeclarationSyntax` (including `record struct`),
  `StructDeclarationSyntax`, `EnumDeclarationSyntax`,
  `MethodDeclarationSyntax`, `ConstructorDeclarationSyntax`,
  `PropertyDeclarationSyntax`, and `FieldDeclarationSyntax`.
- For each declared `ISymbol`, compute a **global symbol key**:
  `symbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)`
  combined with the containing assembly's simple name (to disambiguate
  identically-named types across unrelated assemblies, e.g. two different
  `NuGet` packages both defining `Result`). This string is what makes
  cross-project symbol identity work without relying on symbol
  reference-equality.
- Emit an `ArchNode` for each declared symbol (see Section 4) and register
  `(globalSymbolKey → nodeId)` in an in-memory `SymbolRegistry` shared across
  all projects (thread-safe `ConcurrentDictionary`).
- Also emit `Contains` edges for the structural hierarchy discovered so far
  (Solution contains Project, Project contains Namespace, Namespace contains
  Class, Class contains Method, etc.) — these don't require cross-project
  resolution and can be emitted immediately.
- Partial classes are merged: `GetDeclaredSymbol` already returns one
  symbol for all partial declarations, so no special-casing is needed beyond
  making sure the walker doesn't emit duplicate nodes — the `SymbolRegistry`
  naturally de-dupes on the global symbol key.

**Pass 2 — Relationship resolution.** Once all projects have completed Pass
1 and the `SymbolRegistry` is fully populated, re-walk the same syntax
trees (semantic models are still cached by the workspace, so this is cheap)
and resolve:

- `Implements`: for each type symbol, `namedType.AllInterfaces`, each
  resolved via global symbol key lookup.
- `Inherits`: `namedType.BaseType` chain (stopping at `object`), same
  lookup.
- `Calls`: for each `InvocationExpressionSyntax` inside a method body,
  `semanticModel.GetSymbolInfo(invocation).Symbol` gives the target
  `IMethodSymbol`; resolve its containing type via global symbol key. Where
  `GetSymbolInfo` returns ambiguous `CandidateSymbols` (overload resolution
  failure, usually because of a load error), record the edge with
  `ResolutionConfidence = "Heuristic"` using the first candidate rather than
  dropping it.
- `Injects`: identified from constructor parameters (see 3.4) and DI
  registration call sites (see 3.5).
- `References`: project-level edges from `ProjectReference`/
  `MetadataReference`, and type-level edges wherever a member signature
  (parameter, return type, property type, field type) mentions a symbol
  declared in a different project.
- `Uses`: a catch-all, lower-confidence relationship for symbol
  references that don't fit `Calls`/`Injects`/`Implements`/`Inherits` — e.g.
  a type mentioned only in a generic type argument, an attribute
  application, or a `typeof()` expression.
- `Publishes`/`Consumes`: from MediatR and message-queue heuristics (3.4,
  3.6).
- `Owns`: EF entity ownership (a `DbContext`'s `DbSet<T>` "owns" `T`;
  an aggregate root class "owns" a value object referenced only through it)
  — heuristic, lower confidence than structural edges.

Running resolution as an explicit second pass (rather than trying to
resolve everything inline during discovery) means forward references work
correctly regardless of `scanOrder` — e.g. `Domain` declares
`IOrderRepository`, `Infrastructure` implements it; if `Infrastructure` were
(mis)configured to scan before `Domain` finishes, an inline single-pass
design would either miss the edge or require awkward retry logic. The
two-pass design makes ordering irrelevant to correctness, which is also
exactly what Phase 3's incremental design needs (Section 6 reuses the
persisted `SymbolRegistry` instead of rebuilding it from scratch).

### 3.4 Concrete detection heuristics

Each heuristic below is implemented as its own class under
`Heuristics/<Area>/`, taking a `Compilation`/`SemanticModel` and the shared
`SymbolRegistry`, and returning `ArchNode`/`ArchEdge` fragments. Heuristics
are independent and additive — turning one off (via a future config rule)
must not break another.

| Area | Node types produced | Detection rule |
|---|---|---|
| **Controllers** | `Controller`, `Endpoint` | Class whose `BaseType` chain includes `Microsoft.AspNetCore.Mvc.ControllerBase`/`Controller`, **or** is annotated `[ApiController]`. Each public method decorated with `[HttpGet]`/`[HttpPost]`/`[HttpPut]`/`[HttpDelete]`/`[HttpPatch]`/`[Route]` becomes an `Endpoint` node; the route template is read from the attribute's constructor argument (constant string) combined with any class-level `[Route]` prefix. |
| **Minimal APIs** | `Endpoint` | Syntax-level scan (not symbol-declaration based, since these are invocation expressions, not declarations) for `InvocationExpressionSyntax` whose target method name is `MapGet`/`MapPost`/`MapPut`/`MapDelete`/`MapPatch`/`MapMethods` on a receiver typed `IEndpointRouteBuilder` (resolved via `semanticModel.GetTypeInfo(receiver)`). Route template = first string-literal (or const) argument. The delegate/method-group argument is resolved to an `IMethodSymbol` to link the endpoint to its handler body for `Calls` edges. |
| **MediatR handlers** | `MediatRHandler` | `INamedTypeSymbol` whose `AllInterfaces` contains a constructed interface with `OriginalDefinition` display name `MediatR.IRequestHandler<TRequest, TResponse>` or the one-generic-parameter overload `MediatR.IRequestHandler<TRequest>`, or `MediatR.INotificationHandler<TNotification>`. Type parameters are extracted from the constructed interface's `TypeArguments` and resolved via the global symbol key to link handler → request/notification. |
| **MediatR requests** | `MediatRRequest` | Type whose `AllInterfaces` contains `MediatR.IRequest`, `MediatR.IRequest<TResponse>`, or `MediatR.INotification`. |
| **Domain events** | `DomainEvent` | Type implementing `MediatR.INotification` **and** matching a configurable naming convention (`*Event`/`*DomainEvent` suffix) or living in a namespace ending in `.Events`/`.DomainEvents`. Naming convention is a confidence booster, not a hard requirement — a type is still emitted as a `DomainEvent` candidate on interface match alone, tagged with `Properties["namingConventionMatch"] = "false"` when the name doesn't fit, so false negatives don't silently disappear. |
| **EF entities** | `EFEntity`, `EFDbContext` | Any class type whose `DbContext`-derived class exposes a `DbSet<T>` property — walk each `DbContext` subtype's properties, for each `PropertySymbol` of type `Microsoft.EntityFrameworkCore.DbSet<TEntity>`, resolve `TEntity` and mark it `EFEntity`, with an `Owns` edge from the `EFDbContext` node. Fallback heuristic when no `DbContext` is scannable in-solution (e.g. referenced only as a compiled assembly): classes decorated with `[Table]`/`[Key]`/`[Column]` from `System.ComponentModel.DataAnnotations.Schema`. |
| **Repositories** | `Repository` | Class whose name matches `*Repository` **and** implements an interface named `I*Repository` (name-symmetry heuristic), **or** is registered in DI (see below) against such an interface. Confidence recorded: `"NamingAndInterface"` > `"DIRegistrationOnly"` > `"NamingOnly"`. |
| **Services** | `Service` | Same pattern as Repositories but for `*Service`/`I*Service`, plus explicit recognition of classes registered against an interface via `AddScoped`/`AddTransient`/`AddSingleton` regardless of naming, so business services that don't follow the naming convention are still captured (lower confidence tag if naming doesn't match). |
| **Background workers / hosted services** | `BackgroundWorker`, `HostedService` | Class whose `BaseType` chain includes `Microsoft.Extensions.Hosting.BackgroundService` → `BackgroundWorker`. Class implementing `Microsoft.Extensions.Hosting.IHostedService` directly (not via `BackgroundService`) → `HostedService`. |
| **Message queues** | `MessageQueue`, edges `Publishes`/`Consumes` | (a) MassTransit-style: class implementing `MassTransit.IConsumer<TMessage>` → `Consumes` edge to `TMessage`; any `IPublishEndpoint`/`IBus` field/parameter with a `.Publish<T>()`/`.Send<T>()` call site in a method body → `Publishes` edge, message type resolved from the call's generic type argument or argument expression type. (b) Azure Service Bus / RabbitMQ.Client raw usage: field/property typed `ServiceBusClient`, `ServiceBusSender`, `IModel`/`IChannel` — flagged as a `MessageQueue`-adjacent node with lower confidence since the message *type* often can't be statically resolved (raw byte payloads); recorded as `Uses` rather than `Publishes`/`Consumes` unless a strongly-typed wrapper is detected. |
| **DI registrations** | edge `Injects`, metadata on `Service`/`Repository` nodes | Syntax-level scan for invocation expressions targeting `IServiceCollection` extension methods (`AddScoped`, `AddTransient`, `AddSingleton`, and their non-generic `(Type, Type)` overloads). Generic form: type arguments resolved directly via `semanticModel.GetSymbolInfo`. Non-generic `typeof(X), typeof(Y)` form: resolved from the `typeof` expressions' operand types. Constructor-parameter based `Injects` edges are resolved independently and don't require DI-registration detection to succeed: for every constructor, each parameter whose type is an interface or class resolved via the `SymbolRegistry` produces an `Injects` edge from the containing class to the parameter type, tagged `Properties["viaConstructor"] = "true"`. This constructor-based edge is the higher-confidence signal; the `AddXxx<T,U>()` call site is what lets us also emit the concrete `Owns`-style implementation mapping (interface → concrete type) that constructor injection alone cannot reveal (you can see *that* something implementing `IOrderRepository` is injected, but not *which* concrete type, without the registration). |
| **Configuration** | `ConfigurationSetting` | Types injected as `IOptions<T>`, `IOptionsSnapshot<T>`, `IOptionsMonitor<T>` (constructor parameter unwrapping the generic argument), and call sites of `services.Configure<T>(...)` / `configuration.GetSection("X").Bind<T>()` (section-name string literal captured into `Properties["configSection"]`). |
| **Tests** | `Test`, project-level `IsTestProject` flag | Project-level: project references `xunit`/`NUnit`/`MSTest.TestFramework` package, or project name matches `*.Tests`/`*.Test`/`*.UnitTests`/`*.IntegrationTests`. Method-level: methods decorated `[Fact]`, `[Theory]`, `[Test]`, `[TestMethod]`, `[TestCase]`. A `Uses`/`Calls` edge from the test method to whatever production symbols it directly invokes gives the "which tests cover this class" relationship the Impact Analysis dashboard view needs. |

### 3.5 Confidence scoring

Every heuristic-derived edge (as opposed to a structural edge like
`Contains` or a directly-resolved `Implements`/`Inherits`) carries a
`ResolutionConfidence` value: `"Resolved"` (semantic-model-certain),
`"Heuristic"` (pattern-matched, generally correct but not proven), or
`"Unresolved"` (detected as *something* relevant but the target symbol
couldn't be pinned down — e.g. a raw message-queue payload type). This is
carried in the edge DTO (Section 4) rather than being a separate concept, so
downstream consumers (Graph Store, dashboard) can filter/style low-confidence
edges without the Scanner needing a second output channel.

### 3.6 Config schema ownership

The YAML config shown in the README (`solution`, `scanOrder`, `ignore`,
`languages`, `rules`) is **schema-owned by the Scanner** — it's the primary
and most demanding consumer (it drives project load order, ignore globs, and
which heuristic families run). However:

- The CLI plan will need to reference the same schema for `arch init` /
  `arch doctor` config validation.
- A future Configuration document may want to extend the schema (e.g. adding
  Graph Store connection settings alongside scan settings) in the same
  `arch.yml` file.

**Decision for this plan:** the Scanner ships a strongly-typed
`ScanConfig` class plus a JSON Schema
(`ArchScanner.Contracts/schema/arch-scan-config.schema.json`) generated from
it, published as part of the `ArchScanner.Contracts` package so the CLI and
any future config tooling validate against the same source of truth instead
of hand-copying the YAML shape. This is flagged again in
[Section 9](#9-risks--open-questions) as needing sign-off from whoever owns
the CLI plan, since a single shared `arch.yml` covering both scan config and
other platform settings is a plausible alternative design.

```yaml
# Config shape this plan implements (matches README example, typed below)
solution: PatternVision.sln

scanOrder:
  - Common
  - Domain
  - Application
  - Infrastructure
  - API
  - Tests

ignore:
  - bin
  - obj
  - node_modules

languages:
  - csharp

rules:
  followInheritance: true
  followDI: true
  followMediatR: true
  followProjectReferences: true
```

```csharp
public sealed class ScanConfig
{
    public required string Solution { get; init; }
    public IReadOnlyList<string> ScanOrder { get; init; } = [];
    public IReadOnlyList<string> Ignore { get; init; } = ["bin", "obj"];
    public IReadOnlyList<string> Languages { get; init; } = ["csharp"];
    public ScanRules Rules { get; init; } = new();
}

public sealed class ScanRules
{
    public bool FollowInheritance { get; init; } = true;
    public bool FollowDi { get; init; } = true;
    public bool FollowMediatR { get; init; } = true;
    public bool FollowProjectReferences { get; init; } = true;
}
```

Each `bool` in `ScanRules` gates one heuristic family from Section 3.4
(`FollowMediatR` disables the MediatR/domain-event heuristics entirely,
etc.) so large solutions can opt out of expensive or noisy detectors.

---

## 4. Output Contract

This is the contract the Graph Store team implements against. It is shipped
as its own small, dependency-light class library —
**`ArchScanner.Contracts`** — referenced by both the Scanner and the Graph
Store, so the Graph Store never needs to depend on Roslyn/MSBuild.

### 4.1 Design rules for the contract

1. **IDs are deterministic**, not `Guid.NewGuid()`. `NodeId` is derived by
   hashing the global symbol key (Section 3.3) — or, for non-symbol nodes
   like `Project`/`Solution`, a stable path-based key — through SHA-256,
   hex-encoded, truncated to 32 chars. Same input, same run or a different
   day, same ID. This is what makes Phase 2 diffing (Architecture Timeline
   view) and Phase 3 incremental scans possible.
2. **Nodes and edges are plain, immutable DTOs** (`record`), with no
   behavior, so they serialize trivially to JSON/NDJSON and to whatever
   table shape the Graph Store chooses.
3. **Unknown/extra metadata goes in a `Properties` bag**, not new DTO fields,
   so heuristics can attach detail (HTTP route template, config section
   name, DI lifetime) without the contract needing a new version every time
   a heuristic is refined.
4. **The contract is versioned independently** (`ContractVersion` constant,
   semver) from the Scanner's own release version, since Graph Store and
   Scanner will not always ship in lockstep.

### 4.2 Enums

```csharp
namespace ArchScanner.Contracts;

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
    Endpoint,
    MediatRHandler,
    MediatRRequest,
    DomainEvent,
    EFEntity,
    EFDbContext,
    Repository,
    Service,
    BackgroundWorker,
    HostedService,
    MessageQueue,
    ConfigurationSetting,
    Test,
    DiRegistration,
}

public enum EdgeType
{
    References,
    Calls,
    Implements,
    Inherits,
    Injects,
    Uses,
    Publishes,
    Consumes,
    Owns,
    Contains,
}

public enum ResolutionConfidence
{
    Resolved,
    Heuristic,
    Unresolved,
}
```

### 4.3 Node and edge DTOs

```csharp
namespace ArchScanner.Contracts;

public sealed record ArchNode
{
    /// <summary>Deterministic, content-derived ID. Stable across repeated scans.</summary>
    public required string Id { get; init; }

    public required NodeType Type { get; init; }

    /// <summary>Short display name, e.g. "OrderService".</summary>
    public required string Name { get; init; }

    /// <summary>Fully qualified name where applicable, e.g. "PatternVision.Modules.Orders.OrderService".</summary>
    public string? FullyQualifiedName { get; init; }

    public string? Namespace { get; init; }

    /// <summary>Assembly simple name, e.g. "PatternVision.Modules.Orders.Application".</summary>
    public string? AssemblyName { get; init; }

    /// <summary>Owning MSBuild project name.</summary>
    public string? ProjectName { get; init; }

    /// <summary>Path relative to the solution root, forward-slash-normalized.</summary>
    public string? FilePath { get; init; }

    public int? StartLine { get; init; }
    public int? EndLine { get; init; }

    /// <summary>Method/constructor signature, route template, or other display-relevant text.</summary>
    public string? Signature { get; init; }

    /// <summary>Extensible, heuristic-specific metadata (e.g. "httpMethod", "configSection", "diLifetime").</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; }
        = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>Hash of the declaring syntax span's text, used for incremental change detection.</summary>
    public required string ContentHash { get; init; }

    public required DateTimeOffset ScannedAtUtc { get; init; }
}

public sealed record ArchEdge
{
    public required string Id { get; init; }
    public required string SourceNodeId { get; init; }
    public required string TargetNodeId { get; init; }
    public required EdgeType Type { get; init; }

    /// <summary>Optional human-readable label, e.g. a call-site method name.</summary>
    public string? Label { get; init; }

    public IReadOnlyDictionary<string, string> Properties { get; init; }
        = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>Occurrence count, used for edge-thickness rendering in the dashboard.</summary>
    public double Weight { get; init; } = 1.0;

    public ResolutionConfidence Confidence { get; init; } = ResolutionConfidence.Resolved;
}
```

### 4.4 Scan run metadata and the writer interface

```csharp
namespace ArchScanner.Contracts;

public sealed record ScanRunMetadata
{
    public required string ScanRunId { get; init; }
    public required string SolutionPath { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required string ScannerVersion { get; init; }
    public required string ContractVersion { get; init; }

    /// <summary>"Full" for Phase 1/2 scans, "Incremental" for Phase 3+.</summary>
    public required string ScanKind { get; init; }
}

public sealed record ScanRunSummary
{
    public required string ScanRunId { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required int NodeCount { get; init; }
    public required int EdgeCount { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool Succeeded { get; init; } = true;
}

/// <summary>
/// The contract the Graph Store component implements. The Scanner depends only
/// on this interface, never on a concrete storage technology.
/// </summary>
public interface IGraphWriter
{
    Task BeginScanAsync(ScanRunMetadata metadata, CancellationToken ct = default);

    Task WriteNodesAsync(IReadOnlyCollection<ArchNode> nodes, CancellationToken ct = default);

    Task WriteEdgesAsync(IReadOnlyCollection<ArchEdge> edges, CancellationToken ct = default);

    /// <summary>Used by incremental scans to retract stale nodes before writing replacements.</summary>
    Task DeleteNodesAsync(IReadOnlyCollection<string> nodeIds, CancellationToken ct = default);

    /// <summary>Used by incremental scans to retract edges whose source or target no longer exists.</summary>
    Task DeleteEdgesForNodesAsync(IReadOnlyCollection<string> nodeIds, CancellationToken ct = default);

    /// <summary>
    /// Returns nodeId -> ContentHash for every node currently persisted, so an
    /// incremental scan can diff against the last full/incremental run without
    /// re-parsing unchanged files. Full design in Section 6.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetNodeContentHashesAsync(CancellationToken ct = default);

    Task CompleteScanAsync(ScanRunSummary summary, CancellationToken ct = default);
}
```

Notes for the Graph Store implementer:

- `WriteNodesAsync`/`WriteEdgesAsync` may be called multiple times per scan
  (the Scanner streams in per-project batches rather than buffering the
  whole solution in memory) — treat each call as an upsert keyed by `Id`,
  not an append.
- `DeleteNodesAsync`/`DeleteEdgesForNodesAsync` exist from Phase 1 even
  though only Phase 3 (incremental scans) exercises them non-trivially in
  practice — implement them from day one so the interface doesn't change
  shape later.
- `GetNodeContentHashesAsync` is the one read-path method on an otherwise
  write-only interface; it's what lets the Scanner (specifically the Phase 3
  incremental path) avoid keeping its own separate hash store in sync with
  the Graph Store's.

### 4.5 File-based transport (`NdjsonGraphWriter`)

For debugging, CI golden-file tests, and any consumer that isn't ready to
implement `IGraphWriter` yet, the Scanner also ships a reference
implementation that writes newline-delimited JSON:

```
scan-output/
  manifest.json       # ScanRunMetadata + ScanRunSummary
  nodes.ndjson         # one ArchNode per line
  edges.ndjson         # one ArchEdge per line
```

This is not a second contract — it's `IGraphWriter` implemented against the
filesystem, using the exact same DTOs, so it's a faithful integration-test
double for whatever the Graph Store does with the same data.

### 4.6 Mermaid export (Phase 2 need)

```csharp
namespace ArchScanner.Contracts;

public interface IGraphExportFormatter
{
    string ToMermaid(IReadOnlyCollection<ArchNode> nodes, IReadOnlyCollection<ArchEdge> edges);
}
```

`ToMermaid` sanitizes `ArchNode.Id` into a Mermaid-legal identifier
(alphanumeric + underscore) via a stable substitution table, and renders
`ArchEdge.Type` as the edge label (`-->|Injects|`) so `arch diagram Business`
and the dashboard's export button share one implementation.

---

## 5. Project/Module Structure

```
src/
  ArchScanner.Contracts/              # Section 4 DTOs + IGraphWriter + IGraphExportFormatter
    Model/
      ArchNode.cs
      ArchEdge.cs
      NodeType.cs
      EdgeType.cs
      ResolutionConfidence.cs
    Runs/
      ScanRunMetadata.cs
      ScanRunSummary.cs
    IGraphWriter.cs
    IGraphExportFormatter.cs
    schema/
      arch-scan-config.schema.json    # generated from ScanConfig

  ArchScanner.Core/
    Configuration/
      ScanConfig.cs
      ScanRules.cs
      ScanConfigLoader.cs             # YAML -> ScanConfig, validates against schema
    Workspace/
      MsBuildBootstrapper.cs          # MSBuildLocator.RegisterDefaults() wrapper
      SolutionLoader.cs               # OpenSolutionAsync, WorkspaceFailed handling
      ScanOrderPlanner.cs             # bucket projects into scanOrder, Section 3.2
    Discovery/
      ArchDeclarationWalker.cs        # CSharpSyntaxWalker, Pass 1
      SymbolRegistry.cs               # global symbol key -> nodeId, thread-safe
      NodeIdFactory.cs                # deterministic hashing, Section 4.1
    Resolution/
      RelationshipResolver.cs         # Pass 2 driver
      ImplementsInheritsResolver.cs
      CallsResolver.cs
      ReferencesResolver.cs
    Heuristics/
      WebApi/
        ControllerDetector.cs
        MinimalApiDetector.cs
      Mediator/
        MediatRHandlerDetector.cs
        DomainEventDetector.cs
      EntityFramework/
        EfEntityDetector.cs
      Repositories/
        RepositoryServiceDetector.cs
      DependencyInjection/
        DiRegistrationDetector.cs
      BackgroundProcessing/
        HostedServiceDetector.cs
      Messaging/
        MessageQueueDetector.cs
      Configuration/
        ConfigurationBindingDetector.cs
      Testing/
        TestDetector.cs
    Output/
      NdjsonGraphWriter.cs
      MermaidGraphExportFormatter.cs
      ScanPipeline.cs                 # orchestrates Bootstrap -> Load -> Pass1 -> Pass2 -> Write
    Incremental/                      # Phase 3, stubbed from Phase 1 (interfaces only)
      IIncrementalScanner.cs
      ScanDelta.cs
      SymbolIndexCache.cs
    Metrics/                          # Phase 3
      IArchitectureMetricProvider.cs
      CouplingMetricProvider.cs
      ComplexityMetricProvider.cs
      CircularDependencyDetector.cs
      MetricSnapshot.cs
    LanguagePlugins/                  # Phase 4
      ILanguageScanner.cs
      CSharpLanguageScanner.cs
      LanguageScannerRegistry.cs

  ArchScanner.Cli/
    Program.cs                        # `arch scan` command wiring (System.CommandLine)
    ScanCommand.cs

tests/
  ArchScanner.Core.Tests/
    Heuristics/
      MediatRHandlerDetectorTests.cs
      ControllerDetectorTests.cs
      ... (one file per detector)
    Resolution/
      TwoPassResolutionTests.cs
    Incremental/
      IncrementalEquivalenceTests.cs
  ArchScanner.Snapshots/
    golden/
      sample-erp-solution.graph.golden.json
  samples/
    SampleErpSolution/                # small multi-project solution fixture
      Common/ Domain/ Application/ Infrastructure/ Api/ Tests/
```

`ArchScanner.Contracts` is deliberately the *only* project the Graph Store
repository needs to reference. Everything under `ArchScanner.Core` can
change freely without breaking the Graph Store as long as the contract
project's public surface stays stable (semver-guarded).

---

## 6. Incremental Scanning Design (Phase 3)

### 6.1 Goals

- Given a small set of changed files (supplied by the Incremental Watcher
  component after it detects filesystem changes, or by a `git diff` in CI),
  recompute only the nodes/edges that could have changed — not the whole
  solution.
- Produce a graph **identical** to what a full re-scan would produce (this
  equivalence is a testable acceptance criterion, see Section 8).

### 6.2 API surface

```csharp
namespace ArchScanner.Core.Incremental;

public sealed record ScanDelta
{
    public required IReadOnlyList<string> ChangedFilePaths { get; init; }
    public required IReadOnlyList<string> DeletedFilePaths { get; init; }
}

public interface IIncrementalScanner
{
    Task<ScanRunSummary> ScanChangedAsync(ScanDelta delta, CancellationToken ct = default);
}
```

The Incremental Watcher is responsible for turning raw filesystem events
into a `ScanDelta` (debouncing rapid saves, filtering to `.cs` files, etc.)
— the Scanner only needs a clean list of changed/deleted paths.

### 6.3 Persisted symbol index

Doing two-pass resolution (Section 3.3) from scratch on every incremental
run would defeat the purpose, since Pass 1 requires visiting every
declaration in the solution to build a complete `SymbolRegistry`. Instead:

- After every full or incremental scan, the Scanner persists a
  **symbol index cache** — a serialized form of the `SymbolRegistry`
  (`globalSymbolKey → nodeId, declaringFilePath, contentHash`) — to
  `.arch/scan-cache/symbol-index.json` (or wherever `IGraphWriter` chooses
  to keep it; the Scanner's default `NdjsonGraphWriter` keeps it alongside
  `manifest.json`).
- An incremental run loads this cache instead of re-running Pass 1
  solution-wide.

### 6.4 Incremental algorithm

1. **Load cache.** Read the persisted symbol index. If missing or its
   `ScannerVersion`/`ContractVersion` doesn't match the current build,
   fall back to a full scan (safety net — stale caches are worse than a
   slower scan).
2. **Determine affected projects.** Map each changed file to its owning
   MSBuild project (from the solution's project list), then compute the
   set of projects that transitively reference any project containing a
   changed file (using the MSBuild project-reference graph, not
   `scanOrder`) — call this `ImpactedProjects`. Only `ImpactedProjects`'
   compilations are (re)loaded; everything else is served from the cache.
3. **Re-run Pass 1 for changed files only.** For each changed file, re-walk
   just that file's syntax tree, producing a fresh set of `ArchNode`s.
   Compare each new node's `ContentHash` against the cached hash for the
   same `globalSymbolKey`:
   - Unchanged hash → skip (no-op, avoids needless writer churn).
   - Changed or new hash → stage for write; update the in-memory
     `SymbolRegistry` entry.
   - A symbol present in the cache but absent from the re-walked file →
     stage its node (and all edges where it's source or target) for
     deletion (handles renames/removals).
4. **Re-run Pass 2 for the blast radius.** The "blast radius" is: every
   node staged for write/delete in step 3, plus every node in
   `ImpactedProjects` that has an existing edge (from the Graph Store, via
   `GetNodeContentHashesAsync` plus a corresponding edge-lookup the Graph
   Store exposes, or from the local cache's recorded edges) touching a
   changed node. Only these nodes' relationship resolution is re-run,
   using the merged (cache + freshly updated) `SymbolRegistry` as the
   global lookup — this is why Pass 2 in the full-scan design already
   depends only on the registry and not on re-walking unrelated files.
5. **Write deltas.** Call `DeleteNodesAsync`/`DeleteEdgesForNodesAsync` for
   removed/renamed symbols, then `WriteNodesAsync`/`WriteEdgesAsync` for
   everything staged, then `CompleteScanAsync` with
   `ScanRunMetadata.ScanKind = "Incremental"`.
6. **Persist updated cache.** Write the merged symbol index back out.

### 6.5 Recalculating affected dependencies

The roadmap explicitly calls for "recalculating only affected
dependencies." Step 4 above is the mechanism: because every edge is stored
with explicit `SourceNodeId`/`TargetNodeId`, "affected dependencies" is
simply the edge set incident to any node in the changed/blast-radius set —
no separate dependency-recalculation pass is needed beyond re-running the
same Pass 2 resolvers scoped to that smaller node set.

### 6.6 Metrics and circular dependency detection (also Phase 3)

- `IArchitectureMetricProvider` runs as a post-scan step (full or
  incremental) over the current node/edge set:
  - **Coupling**: afferent (incoming `References`/`Uses`/`Calls` edge count)
    and efferent (outgoing) counts per `Project` and `Namespace` node,
    emitted as a `MetricSnapshot`.
  - **Complexity**: cyclomatic complexity per `Method` node, computed by
    counting decision points (`if`, `else if`, `case`, `&&`, `||`, `?:`,
    loops, `catch`) in the method's syntax subtree during Pass 1 (cheap,
    done once, cached in `ArchNode.Properties["cyclomaticComplexity"]`
    rather than requiring a separate metrics pass over source).
  - **Circular dependencies**: Tarjan's strongly-connected-components
    algorithm run over the `References` edge subgraph at `Project`
    granularity (primary use case — project-level cycles are the ones that
    actually block builds) and optionally at `Namespace` granularity
    (noisier, opt-in). Each SCC of size > 1 is reported as a
    `MetricSnapshot` entry with the participating node IDs and the cycle
    edges, which the dashboard's Coupling Heatmap and a future `arch
    doctor` check both consume.
- On an incremental scan, metrics for unaffected projects are **not**
  recomputed — they're carried forward from the last snapshot — except
  cycle detection, which must re-run over the full current edge set
  whenever any `References` edge changed, since a cycle can newly form
  between two projects neither of which individually changed relative to
  each other (a third project's edge change can close a cycle). This is
  called out explicitly because it's the one metric that can't be scoped
  to the blast radius the same way node/edge diffing can.

```csharp
namespace ArchScanner.Core.Metrics;

public sealed record MetricSnapshot
{
    public required string ScanRunId { get; init; }
    public required string SubjectNodeId { get; init; }
    public required string MetricName { get; init; }   // "AfferentCoupling", "EfferentCoupling",
                                                          // "CyclomaticComplexity", "CircularDependency"
    public required double Value { get; init; }
    public IReadOnlyList<string> RelatedNodeIds { get; init; } = []; // e.g. cycle participants
    public required DateTimeOffset ComputedAtUtc { get; init; }
}
```

---

## 7. Multi-language Extensibility Design (Phase 4)

### 7.1 Extraction, not addition

Phase 4 does not add scanning capability for a new language — it
**refactors** the existing, working C# pipeline behind an abstraction so a
future language scanner is a plug-in rather than a rewrite. This is
deliberately sequenced last (Phase 4) because designing the abstraction
correctly requires having already built one full, real implementation
(C#/Roslyn) — an abstraction designed before Phase 1 ships would be
guessing at the wrong seams.

### 7.2 `ILanguageScanner`

```csharp
namespace ArchScanner.Core.LanguagePlugins;

public interface ILanguageScanner
{
    /// <summary>Config key from ScanConfig.Languages, e.g. "csharp", "typescript".</summary>
    string LanguageId { get; }

    bool CanHandle(ProjectDescriptor project);

    Task<LanguageScanResult> DiscoverAsync(ProjectDescriptor project, CancellationToken ct);

    Task<LanguageScanResult> ResolveRelationshipsAsync(
        LanguageScanResult discovery,
        SymbolRegistry globalRegistry,
        CancellationToken ct);
}

public sealed record ProjectDescriptor
{
    public required string ProjectName { get; init; }
    public required string ProjectFilePath { get; init; }   // .csproj, package.json, pyproject.toml, ...
    public required IReadOnlyList<string> SourceFiles { get; init; }
}

public sealed record LanguageScanResult
{
    public required IReadOnlyList<ArchNode> Nodes { get; init; }
    public required IReadOnlyList<ArchEdge> Edges { get; init; }
}
```

`CSharpLanguageScanner` wraps the exact Section 3 pipeline
(`SolutionLoader` + `ArchDeclarationWalker` + `RelationshipResolver`) behind
this interface with no behavior change. `ScanOrderPlanner` and the
`ScanPipeline` orchestrator are updated to dispatch through a
`LanguageScannerRegistry` (keyed by `ScanConfig.Languages` and
`ProjectDescriptor` file patterns — `.csproj` → csharp, `package.json` +
`tsconfig.json` → typescript) instead of assuming C# everywhere.

### 7.3 Keeping the node/edge vocabulary language-agnostic

`NodeType` (Section 4.2) already leans generic (`Class`, `Interface`,
`Method`) rather than C#-specific, which is intentional groundwork for this
phase. Concepts with no clean C# equivalent in other ecosystems are still
representable:

- TypeScript `interface`/`type` → `Interface`/`Class` (best fit) with
  `Properties["languageConstruct"] = "type-alias"` where the fit is
  imperfect, rather than growing the enum per language.
- JavaScript/TypeScript modules → `Namespace` node (structural container),
  keeping the `Contains` hierarchy consistent across languages so the
  dashboard's Repository Explorer doesn't need per-language rendering
  logic.
- Framework-specific nodes (`Controller`, `MediatRHandler`, etc.) remain
  strictly opt-in per language scanner — a `TypeScriptLanguageScanner`
  simply never emits a `MediatRHandler` node; it isn't required to
  understand every C# concept.

### 7.4 Out-of-process plugin protocol (forward-looking, not built in Phase 4)

Not every future language scanner should have to be a .NET assembly loaded
in-process (a Python or TypeScript parser is far more naturally implemented
in its native ecosystem, e.g. `ts-morph` for TypeScript). Phase 4's design
therefore also specifies — without necessarily shipping an implementation —
an **out-of-process variant** of `ILanguageScanner`:

- A plugin is any executable that, given a `ProjectDescriptor` (passed as
  JSON on stdin or as a temp file path argument), emits NDJSON on stdout
  matching exactly the `ArchNode`/`ArchEdge` shapes from Section 4 (the
  same wire format `NdjsonGraphWriter` already uses, reused deliberately so
  there's only one serialization contract in the whole system).
- A thin `ExternalProcessLanguageScanner : ILanguageScanner` adapter shells
  out to the configured executable (path from `ScanConfig`, e.g.
  `languages: [{ id: typescript, command: "arch-scan-ts" }]` — a config
  schema extension, flagged as a Phase 4 config change) and parses its
  NDJSON output back into `LanguageScanResult`.
- Cross-language relationship resolution (Pass 2, Section 3.3) still needs
  a *shared* `SymbolRegistry`, which is straightforward for edges within a
  single language but an open question for cross-language edges (e.g. a
  C# controller calling into a TypeScript-generated client) — flagged in
  Section 9 as unresolved; Phase 4 scope here is the plugin protocol and
  registry shape, not solving cross-language symbol resolution.

### 7.5 Quality scoring inputs

The roadmap's Phase 4 "architecture quality scoring" is explicitly a
separate scoring engine's responsibility. The Scanner's job is to make sure
enough raw signal exists in the graph for that engine to compute a score
from, without the Scanner itself deciding what "good" means:

- Coupling and complexity `MetricSnapshot`s (already emitted in Phase 3).
- Circular dependency counts/participants (Phase 3).
- Test-to-production edge density: ratio of `Test` nodes with a `Calls`/
  `Uses` edge into a given `Class`/`Method` vs. that node's total member
  count — a coverage *proxy*, not a substitute for real code-coverage
  tooling, explicitly labeled as such in `Properties`.
- Layering-violation candidates: edges that cross `scanOrder` layers in the
  "wrong" direction (e.g. `Domain` referencing `Infrastructure`) — flagged
  as `Properties["layeringViolation"] = "true"` on the offending edge
  during Pass 2, using the same layer-bucketing `ScanOrderPlanner` already
  computes in Section 3.2, at effectively no extra cost.

---

## 8. Testing Strategy

### 8.1 Unit tests (heuristics, in isolation)

- Each detector in `Heuristics/` gets its own test class that builds a
  minimal in-memory `CSharpCompilation` from string literals via
  `CSharpCompilation.Create(...)` and hand-written `SyntaxTree.ParseText(...)`
  — no `MSBuildWorkspace`, no disk I/O, fast and deterministic. Example
  target: `MediatRHandlerDetectorTests` compiles a two-file snippet (a
  `record CreateOrder : IRequest<Guid>` and a
  `class CreateOrderHandler : IRequestHandler<CreateOrder, Guid>`) and
  asserts the detector emits a `MediatRHandler` node linked to a
  `MediatRRequest` node via the handler-to-request relationship.
- Cover both the positive case and at least one near-miss per detector
  (e.g. a class named `OrderRepository` that does *not* implement
  `IOrderRepository`, to verify confidence tagging rather than a false
  "Resolved" classification).

### 8.2 Integration tests (full pipeline against sample solutions)

- `tests/samples/SampleErpSolution/` is a small, checked-in, real multi-
  project `.sln` mirroring the layering shown in the README config example
  (`Common`, `Domain`, `Application`, `Infrastructure`, `Api`, `Tests`),
  deliberately including at least one instance of every heuristic in
  Section 3.4 (a controller, a minimal API endpoint, a MediatR
  handler/request pair, a domain event, an EF entity + DbContext, a
  repository + interface, a DI-registered service, a `BackgroundService`,
  an `IHostedService`, a MassTransit consumer, an `IOptions<T>` consumer,
  and an xUnit test) plus one deliberate project-reference cycle for the
  Phase 3 circular-dependency detector to catch.
- Run through the real `ScanPipeline` (MSBuildWorkspace included) in CI —
  slower than unit tests, run as a separate CI stage.

### 8.3 Golden-file / snapshot tests

- Serialize the full `IReadOnlyList<ArchNode>`/`IReadOnlyList<ArchEdge>`
  output for `SampleErpSolution`, sorted by `Id` for determinism, to JSON,
  and compare against a checked-in golden file
  (`tests/ArchScanner.Snapshots/golden/sample-erp-solution.graph.golden.json`)
  using a snapshot library (e.g. Verify.Xunit) that produces a clear diff
  on mismatch and supports a `--accept`-style workflow for intentional
  updates.
- Volatile fields (`ScannedAtUtc`, `ScanRunId`) are normalized/scrubbed
  before comparison — the point of the golden file is catching accidental
  changes to *structure*, not timestamps.
- This snapshot suite is the primary regression guard when heuristics are
  refined — any change to the golden file must be a reviewed, intentional
  diff in the PR.

### 8.4 Determinism tests

- Run the full pipeline twice against the same solution snapshot and
  assert byte-identical (post-normalization) output. This is a hard
  requirement (Section 4.1), not a nice-to-have, and deserves its own
  always-on CI test rather than being implied by the golden-file test
  alone (a golden-file test only proves stability against *one* previous
  run, not that two runs *right now* agree with each other).

### 8.5 Incremental equivalence tests (Phase 3 acceptance criterion)

- For a representative set of mutations against `SampleErpSolution`
  (add a class, delete a class, rename a method, add a constructor
  parameter/new DI dependency, introduce a new circular project reference),
  assert: `IncrementalScan(delta)` output, merged onto the prior graph,
  equals `FullScan()` output on the mutated solution, node-for-node and
  edge-for-edge. This is the single most important test in the Phase 3
  suite — it's what proves the incremental path isn't silently dropping
  edges.

### 8.6 Performance regression tests

- A generated synthetic large solution (script-generated, not checked in
  wholesale — a fixture generator producing e.g. 300 projects / ~5,000
  classes) with a rough wall-clock budget asserted in CI (generous
  threshold, flagged as needing real hardware calibration once Phase 1 is
  running) to catch accidental quadratic-behavior regressions (e.g. an
  `O(n²)` symbol lookup creeping into `SymbolRegistry`).

### 8.7 Multi-language plugin contract tests (Phase 4)

- A fake `ILanguageScanner` test double (no real TypeScript/Python parser
  needed) verifies `LanguageScannerRegistry` dispatch logic — right
  scanner picked per `ProjectDescriptor`, and that `Contains`/structural
  edges from a mixed-language solution merge correctly with C#'s.
- A test double `ExternalProcessLanguageScanner` target (a trivial fake
  script that just echoes fixed NDJSON) verifies the out-of-process
  protocol parsing (Section 7.4) end-to-end without depending on any real
  external tool.

---

## 9. Risks & Open Questions

1. **MSBuildWorkspace reliability on CI/varied environments.** Version
   mismatches between `Microsoft.Build.Locator` and the installed SDK,
   partial restores, and multi-targeted projects (`TargetFrameworks` with
   multiple TFMs) can all cause load failures or duplicate compilations.
   Open question: for a multi-targeted project, do we scan every TFM (and
   de-duplicate resulting nodes by global symbol key) or only the first/
   configured TFM? Leaning toward "first TFM only, configurable" for
   Phase 1, revisit if it causes missed conditional-compilation code.
2. **Memory footprint on large solutions.** Holding every project's
   `Compilation` in memory simultaneously (needed across both passes) may
   not scale past some solution size. Mitigation sketched but not fully
   designed: after Pass 1 completes for a project, keep only the
   lightweight `SymbolRegistry` entries and release the `Compilation`,
   reloading it for Pass 2 only if still needed — needs a memory
   benchmark against a real large solution before committing to this.
3. **Source generators and generated code.** Decision needed on whether
   generator-produced source participates in scanning at all. Current
   plan: exclude anything Roslyn flags as generated
   (`GeneratedCodeAnalysisFlags`/file path heuristics) by default, with a
   `rules.followGeneratedCode` opt-in flag left as a documented gap rather
   than implemented in Phase 1.
4. **Reflection-based DI (Scrutor `Scan(...)`, assembly scanning).** The
   `DiRegistrationDetector` heuristic (Section 3.4) only catches explicit
   `AddScoped<T,U>()`-style call sites. Convention-based/reflection
   registration will under-detect concrete implementation mappings (the
   constructor-injection `Injects` edge still works; the interface→
   concrete-type mapping does not). Flagged as a known accuracy gap, not
   silently swallowed — emit a scan-summary warning when a project
   references Scrutor but has few/no explicit `AddXxx` call sites, so
   `arch doctor` can surface it.
5. **MediatR pipeline behaviors obscure the "real" call path.** A
   controller calling `mediator.Send(request)` never syntactically calls
   the handler directly — the true dispatch happens through MediatR's
   internal pipeline (and any registered `IPipelineBehavior<,>`
   decorators). Decision made in this plan: model this as two edges
   (`Controller --Uses--> MediatRRequest` and
   `MediatRRequest --Consumes--> MediatRHandler`, handler resolved via the
   generic type-argument match in Section 3.4) rather than a direct
   `Calls` edge, to keep the graph honest about the indirection — but this
   means the dependency-graph dashboard view needs to know to "collapse"
   this two-hop pattern visually, which is a dashboard-side follow-up, not
   a scanner change; flagged here so that team is aware.
6. **Config schema ownership.** Section 3.6's decision (Scanner owns
   `ScanConfig`/JSON Schema, published via `ArchScanner.Contracts`) needs
   explicit sign-off from whoever writes the CLI plan and any future
   Configuration-focused document — a single shared `arch.yml` spanning
   scan config *and* other platform settings (Graph Store connection
   string, MCP server settings, etc.) is a real alternative and would
   change where the schema lives.
7. **Node ID stability across scanner upgrades.** If the hashing scheme in
   `NodeIdFactory` ever changes (e.g. a bug fix that changes what counts as
   part of the "global symbol key"), every previously stored node ID
   becomes invalid, breaking the Architecture Timeline view's "diff over
   time" feature. Mitigation: `ContractVersion`/`ScannerVersion` are
   already carried on `ScanRunMetadata`; the Graph Store should treat a
   version bump in the hashing scheme as requiring a full re-scan +
   migration, not an incremental one. This needs an explicit compatibility
   policy written jointly with `02-graph-store.md`.
8. **Cross-language symbol resolution (Phase 4).** Section 7.4 flags this
   directly: once a second language scanner exists, resolving an edge that
   crosses the language boundary (e.g. a C# controller that calls a
   generated TypeScript API client) has no designed solution yet. Likely
   needs a shared, language-agnostic "public contract" concept (OpenAPI
   spec, gRPC/proto definitions) as the bridge rather than symbol-level
   resolution — out of scope to solve now, called out so it isn't
   forgotten when Phase 4 planning starts in earnest.
9. **Confidence-tagged edges need a consumer-side convention.** The
   Scanner emits `ResolutionConfidence`, but nothing in this plan mandates
   how the dashboard/REST API should render or filter `"Heuristic"`/
   `"Unresolved"` edges. Needs a decision from the dashboard/API side
   before Phase 2 ships the Dependency Graph view, or every low-confidence
   edge will render identically to a certain one and erode trust in the
   tool.
10. **Parallel Pass 1/Pass 2 correctness under concurrent writes to
    `SymbolRegistry`.** The design calls for `ConcurrentDictionary`-backed
    registration during a parallelized Pass 1, but Pass 2 must not start
    for *any* project until Pass 1 has completed for *all* projects
    (otherwise forward-reference resolution silently returns "unresolved"
    for a symbol that would have resolved a few milliseconds later). This
    needs a hard synchronization barrier (`Task.WhenAll` gate) in
    `ScanPipeline` — noted here so it isn't lost as an implementation
    detail during a future performance-optimization pass that might
    otherwise be tempted to interleave the two passes per-project.

---

## 10. Task Breakdown

### Phase 1 — Solution Scanner, Dependency Graph, SQLite Output, CLI

- [ ] Scaffold `ArchScanner.Contracts`, `ArchScanner.Core`, `ArchScanner.Cli`
      projects; set up solution and CI build.
- [ ] Implement `NodeType`, `EdgeType`, `ResolutionConfidence` enums,
      `ArchNode`, `ArchEdge`, `ScanRunMetadata`, `ScanRunSummary` DTOs
      (Section 4.2–4.4).
- [ ] Implement `IGraphWriter` interface and publish it as the first stable
      contract surface for the Graph Store team to start against.
- [ ] Implement `ScanConfig`/`ScanRules`, YAML loader, and generate the
      JSON Schema file (Section 3.6).
- [ ] Implement `MsBuildBootstrapper` + `SolutionLoader` with
      `WorkspaceFailed` diagnostics collection.
- [ ] Implement `ScanOrderPlanner` (bucket projects into `scanOrder`,
      unmatched-project warning).
- [ ] Implement `NodeIdFactory` (deterministic hashing) and
      `SymbolRegistry` (thread-safe global symbol key map).
- [ ] Implement `ArchDeclarationWalker` (Pass 1) covering classes,
      interfaces, records, structs, enums, methods, constructors,
      properties, fields, plus `Contains` edge emission.
- [ ] Implement `RelationshipResolver` (Pass 2) for `Implements`,
      `Inherits`, `Calls`, `References`, `Uses`.
- [ ] Implement heuristic detectors: Controllers, Minimal APIs, MediatR
      handlers/requests, Domain Events, EF entities/DbContext,
      Repositories/Services, Background workers/Hosted services, Message
      queues, DI registrations, Configuration bindings, Tests (Section 3.4
      table — one PR per detector recommended).
- [ ] Wire confidence tagging (`ResolutionConfidence`) into every
      heuristic-derived edge.
- [ ] Implement `NdjsonGraphWriter` (reference/debug writer).
- [ ] Implement `ScanPipeline` orchestrator (bootstrap → load → Pass 1
      barrier → Pass 2 → heuristics → write), with the parallel Pass 1 /
      synchronized Pass 2 barrier from Risk #10.
- [ ] Implement `arch scan` CLI command (`ArchScanner.Cli`), wiring config
      path resolution, `IGraphWriter` selection (ndjson vs. sqlite, once
      Graph Store ships one), and summary console output.
- [ ] Build `SampleErpSolution` fixture (Section 8.2) with one instance of
      every heuristic plus a deliberate project cycle.
- [ ] Write unit tests for every heuristic (Section 8.1).
- [ ] Write the first golden-file snapshot test against
      `SampleErpSolution` (Section 8.3).
- [ ] Write the determinism test (Section 8.4 — run twice, compare).
- [ ] Document the `ArchScanner.Contracts` public surface for the Graph
      Store team (API docs / XML doc comments at minimum).

### Phase 2 — Dashboard-Facing Metadata (no new scan capability)

- [ ] Audit `ArchNode`/`ArchEdge` fields against Repository Explorer,
      Dependency Graph view, and Service Explorer requirements; add any
      missing `Properties` keys (e.g. HTTP route templates, DI lifetimes)
      — additive only, no contract version bump needed unless a new
      required field is discovered.
- [ ] Implement `IGraphExportFormatter`/`ToMermaid` (Section 4.6).
- [ ] Verify `Contains` hierarchy is complete and correctly ordered for
      Repository Explorer's project → layer grouping.
- [ ] Add `Weight` computation (call/reference occurrence counting) if not
      already populated in Phase 1, needed for edge-thickness rendering.
- [ ] Coordinate with dashboard team on `ResolutionConfidence` rendering
      convention (Risk #9).

### Phase 3 — Incremental Scanning, Metrics, Circular Dependency Detection

- [ ] Design and implement the persisted symbol index cache format
      (Section 6.3).
- [ ] Implement `IIncrementalScanner`/`ScanDelta` and the changed-file →
      affected-project mapping (Section 6.4 step 2).
- [ ] Implement incremental Pass 1 (single-file re-walk + hash diff) and
      incremental Pass 2 (blast-radius-scoped relationship resolution).
- [ ] Implement cache-staleness fallback to full scan (version mismatch
      detection).
- [ ] Implement `MetricSnapshot` DTO and `IArchitectureMetricProvider`
      pipeline hook.
- [ ] Implement `CouplingMetricProvider` (afferent/efferent per
      project/namespace).
- [ ] Implement cyclomatic complexity computation during Pass 1 (avoid a
      separate full re-walk).
- [ ] Implement `CircularDependencyDetector` (Tarjan's SCC) at project
      granularity, with namespace granularity as an opt-in.
- [ ] Implement the "cycles always re-run on any `References` change"
      rule (Section 6.6) rather than scoping cycle detection to the blast
      radius.
- [ ] Write incremental equivalence tests for the mutation scenarios listed
      in Section 8.5.
- [ ] Write performance regression tests against the generated large-
      solution fixture (Section 8.6).
- [ ] Expose whatever hand-off API the Incremental Watcher component needs
      to call `ScanChangedAsync` (coordinate with that component's plan).

### Phase 4 — Multi-Language Groundwork, Quality Scoring Inputs

- [ ] Extract `ILanguageScanner` interface and refactor the Phase 1
      pipeline into `CSharpLanguageScanner` with no behavior change
      (verified by re-running the Phase 1 golden-file suite unchanged).
- [ ] Implement `LanguageScannerRegistry` and `ProjectDescriptor`-based
      dispatch; extend `ScanConfig.Languages` handling accordingly.
- [ ] Design (spec only, or minimal reference implementation) the out-of-
      process plugin protocol (Section 7.4): NDJSON-over-stdio contract,
      `ExternalProcessLanguageScanner` adapter.
- [ ] Write plugin-contract tests using a fake `ILanguageScanner` and a
      trivial fake external-process script (Section 8.7).
- [ ] Add layering-violation edge tagging (Section 7.5), reusing
      `ScanOrderPlanner`'s layer buckets.
- [ ] Add test-to-production edge density computation as a quality-scoring
      input metric.
- [ ] Document, for the future scoring-engine team, exactly which
      `MetricSnapshot`/`Properties` fields are intended as scoring inputs
      versus purely diagnostic.
- [ ] Revisit and document the cross-language symbol resolution open
      question (Risk #8) with whatever concrete second language is chosen
      first.
