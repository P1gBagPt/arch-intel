# 06 — Next.js Dashboard Implementation Plan

> Component: **Web Dashboard** (`Next.js UI` in the High-Level Architecture diagram)
> Consumes: REST API (see `05-rest-api.md`) and the SignalR live-update hub
> Produced by: this document, standalone and actionable across all four roadmap phases

---

## 1. Overview & Responsibilities

The Dashboard is the human-facing surface of the Architecture Intelligence Platform. Where the MCP Server lets AI agents *query* the architecture graph programmatically, the Dashboard lets developers, tech leads, architects, and engineering managers *see* it — browse it, search it, and reason about change impact visually before writing code.

The Dashboard owns:

* Rendering the architecture graph (projects, services, interfaces, entities, and their relationships) as interactive visualizations at multiple scales — from a handful of nodes (Service Explorer) to tens of thousands (Dependency Graph, Coupling Heatmap).
* Translating REST API responses into UI state via TanStack Query, with caching, background refetch, and optimistic updates.
* Subscribing to the SignalR hub for live graph updates pushed by the Incremental Watcher, and reconciling those pushes into the TanStack Query cache without jarring the user's current view (pan/zoom/selection preserved).
* Providing a natural-language entry point (AI Planner) that calls `POST /implementation-plan` and renders a structured plan.
* From Phase 4 onward, authenticating users and gating multi-repo / team features behind that identity.

The Dashboard does **not** own graph computation, impact analysis logic, or scanning — those live server-side. The Dashboard is a rendering and interaction layer over a well-defined contract; where a needed endpoint or hub method isn't listed below, treat it as *TBD in `05-rest-api.md`* and stub it behind an adapter (see §3.4) so the UI can be built against fixtures ahead of backend availability.

### Non-goals

* No client-side graph algorithms beyond simple layout tweaks (filtering, focus/expand) — traversal, shortest-path, coupling scores, and diffing are server-computed.
* No local persistence of architecture data beyond the TanStack Query cache (no IndexedDB/localStorage graph store in Phase 1–3; revisit for offline/snapshot browsing in Phase 4).
* No direct database or scanner access — the Dashboard is a REST/SignalR client only.

---

## 2. Phase-by-Phase Scope

### Phase 1 — No Dashboard (Backend-Only Phase)

Phase 1 delivers the scanner, dependency graph builder, SQLite storage, CLI, and a basic MCP server. There is **no dashboard work scheduled**, and none should be started against a moving/unstable API. However, doing a small amount of prep now avoids a slow Phase 2 start:

* Reserve the repo path and confirm the monorepo/polyrepo decision (see §8) — this doc assumes the dashboard lives in its own top-level `dashboard/` or a sibling repo `architecture-intelligence-dashboard`.
* Stand up the Next.js app skeleton (`create-next-app` with App Router + TypeScript + Tailwind) **without wiring real data** — this can happen at the tail end of Phase 1 once the CLI (`arch scan`, `arch graph`) can emit a stable JSON shape, so the dashboard team can build against realistic static fixtures (e.g., `arch graph --format json > fixtures/graph.sample.json`) instead of guessing.
* Agree on the API response contracts with whoever owns `05-rest-api.md` — specifically the shape of `/graph`, `/projects`, `/services`, `/impact`, `/metrics` — before Phase 2 begins, since the whole view layer is designed against those shapes.
* No deployment, no auth, no SignalR client work in Phase 1.

Deliverable at end of Phase 1 (dashboard-relevant): an empty-but-building Next.js app in the repo, a fixtures folder with sample scanner output, and a written/agreed API contract to build against.

### Phase 2 — Core Dashboard (Primary Build Phase)

This is where the dashboard becomes real. Scope, directly from the roadmap (*"Next.js dashboard, Interactive dependency graph, Impact analysis, Mermaid export, Architecture explorer"*):

* App shell: layout, navigation, theming, repo/project selector (single-repo only in Phase 2), API client, TanStack Query setup.
* **Repository Explorer** — tree view (Projects / Business / Infrastructure / API / Tests), fully functional against `GET /projects`.
* **Dependency Graph** — interactive view against `GET /graph`, with zoom/pan/filter/search/expand.
* **Service Explorer** — detail view against `GET /services/:id` (dependencies, callers, implementations, tests, interfaces).
* **Impact Analysis** — basic version against `GET /impact?target=...` (class/interface picker → affected list, no highlighting animation polish required yet, but the graph highlight is the core value so it should work end-to-end).
* **Mermaid export** — a "Export as Mermaid" action, likely backed by `POST /diagram` (returns Mermaid text), available from Dependency Graph, Service Explorer, and Impact Analysis views.
* No SignalR yet (or, if the hub exists early, treat it as informational/no-op — full reconciliation lands Phase 3).
* No auth — single implicit "default" workspace.

This phase is the bulk of the effort and is broken into weekly-sized tasks in §12.

### Phase 3 — Live & Intelligent (AI Planner, Timeline, Coupling)

Roadmap: *"AI implementation planner, Incremental watcher, Architecture metrics, Coupling analysis, Circular dependency detection."* Dashboard scope:

* **SignalR client integration** — connect to the live-update hub, reconcile pushed deltas into the TanStack Query cache, surface an unobtrusive "live" indicator and toast/badge on incoming changes.
* **Architecture Timeline** — new view, `GET /metrics` (or a dedicated `/timeline`/`/snapshots` endpoint — see Risks §11) rendered as a delta feed ("+28 classes, +3 projects, -1 interface") plus a trend chart.
* **Coupling Heatmap** — new view, projects colored green/yellow/red from `GET /metrics` coupling scores.
* **AI Planner** — new view/panel, free-text input → `POST /implementation-plan` → structured plan rendering (affected projects, new files, modified services, DB changes, tests required, risk level, effort estimate).
* Circular dependency detection surfaces as a warning badge/filter on the Dependency Graph and Coupling Heatmap (data from `/metrics` or `/graph`), not a standalone view.
* Impact Analysis is upgraded: richer highlighting (animated propagation through the graph), and it becomes the launch point for the AI Planner ("Plan the implementation impact of changing this class").

### Phase 4 — Collaboration & Scale (Future)

Roadmap: *"Cloud synchronization, Team collaboration, Historical architecture snapshots, Multi-repository support, Architecture quality scoring."* Dashboard scope:

* **Authentication UI** — Better Auth-backed sign-in with GitHub OAuth and Microsoft Entra ID, session-aware navigation, protected routes.
* **Multi-repo switcher** — top-level repo/workspace selector; all views become repo-scoped; URL structure gains a repo segment (see §8).
* **Team collaboration** — shareable deep links to a specific graph view/selection, lightweight commenting/annotation on nodes (e.g., "why does this depend on X?"), possibly presence indicators ("2 others viewing this service").
* **Historical snapshot browsing** — a time-travel slider/picker built on top of the Architecture Timeline, letting users load the graph *as of* a past scan.
* **Architecture quality scoring** — a scorecard view (or a widget embedded in Repository Explorer/Coupling Heatmap) visualizing a composite quality score with drill-down into contributing metrics.

---

## 3. Application Architecture

### 3.1 Framework & Routing (Next.js App Router)

Next.js App Router is used throughout, Server Components by default, Client Components only where interactivity (graph canvases, forms, live subscriptions) requires it. Route-per-view, matching the six visualization views plus supporting routes:

```
app/
├── layout.tsx                      # root layout: theme provider, QueryClientProvider, nav shell
├── page.tsx                        # dashboard home / landing (repo summary, quick links)
├── (dashboard)/
│   ├── layout.tsx                  # shared dashboard chrome: sidebar nav, top bar, repo selector (P4)
│   ├── explorer/
│   │   └── page.tsx                # Repository Explorer (tree)
│   ├── graph/
│   │   ├── page.tsx                # Dependency Graph (Cytoscape/Sigma canvas)
│   │   └── [nodeId]/page.tsx       # deep link: graph pre-focused on a node
│   ├── services/
│   │   ├── page.tsx                # Service list / search
│   │   └── [serviceId]/page.tsx    # Service Explorer detail
│   ├── impact/
│   │   ├── page.tsx                # Impact Analysis picker
│   │   └── [targetId]/page.tsx     # Impact Analysis result for a given class/interface
│   ├── timeline/                   # Phase 3
│   │   └── page.tsx
│   ├── coupling/                   # Phase 3
│   │   └── page.tsx
│   ├── planner/                    # Phase 3
│   │   └── page.tsx
│   └── settings/                   # Phase 4: repos, team, account
│       └── page.tsx
├── (auth)/                         # Phase 4
│   ├── sign-in/page.tsx
│   └── callback/[provider]/page.tsx
└── api/
    └── auth/[...all]/route.ts      # Phase 4: Better Auth route handler
```

Notes:

* Route groups `(dashboard)` and `(auth)` keep layouts isolated without affecting URL paths.
* `[nodeId]`, `[serviceId]`, `[targetId]` dynamic segments make every view **deep-linkable** — a prerequisite for Phase 4 sharing/collaboration, so it's designed in from Phase 2 rather than retrofitted.
* Phase 4 adds a leading `/[repoId]/` segment ahead of `(dashboard)` once multi-repo lands; Phase 2–3 routes are written so that inserting this segment is a folder move, not a rewrite (components already receive `repoId` as a prop/param sourced from a single `useRepo()` hook rather than hardcoding "current repo").

### 3.2 State Management — TanStack Query

TanStack Query is the single source of truth for all server state. No Redux/Zustand global store for server data; local UI state (selected node, filter panel open/closed, graph layout mode) uses component state or a small Zustand store scoped to the graph view only (see §3.5).

Conventions:

* One `QueryClient` instance per app, created in a client-side provider (`app/providers.tsx`), with sane defaults:

```ts
// app/providers.tsx
'use client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useState } from 'react';

export function Providers({ children }: { children: React.ReactNode }) {
  const [client] = useState(() => new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,          // architecture data doesn't change every second
        refetchOnWindowFocus: false, // avoid re-fetching a 10k-node graph on tab focus
        retry: 2,
      },
    },
  }));
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}
```

* Query keys follow a hierarchical factory pattern per resource, so SignalR reconciliation (§6) and manual invalidation can target precisely:

```ts
// lib/query-keys.ts
export const queryKeys = {
  projects: {
    all: ['projects'] as const,
    detail: (id: string) => ['projects', id] as const,
  },
  services: {
    all: ['services'] as const,
    detail: (id: string) => ['services', id] as const,
  },
  graph: {
    all: ['graph'] as const,
    filtered: (filters: GraphFilters) => ['graph', filters] as const,
  },
  impact: (targetId: string) => ['impact', targetId] as const,
  metrics: {
    all: ['metrics'] as const,
    coupling: ['metrics', 'coupling'] as const,
    timeline: (range: string) => ['metrics', 'timeline', range] as const,
  },
};
```

* Mutations (`POST /implementation-plan`, `POST /diagram`, `POST /architecture-analysis`) use `useMutation`, are **not** cached as queries (they're actions, not resources), and their results are held in local component/route state for display, optionally persisted to a "recent plans" list in query cache under a synthetic key (`['planner', 'history']`) for a lightweight history panel.
* All hooks live in `hooks/` (e.g., `useProjects`, `useDependencyGraph`, `useImpactAnalysis`, `useImplementationPlan`) — components never call `fetch`/the API client directly.

### 3.3 Data Fetching Patterns

* A thin typed API client (`lib/api-client.ts`) wraps `fetch`, injects base URL (from `NEXT_PUBLIC_API_URL`), attaches auth headers when Phase 4 lands, and normalizes error shapes.
* Server Components fetch initial data for fast first paint on pages that don't need live interactivity in their static shell (e.g., Service list), passed down as `initialData` to hydrate a client-side `useQuery`. Highly interactive canvases (Dependency Graph, Coupling Heatmap) fetch client-side only, since they need the QueryClient's caching/reconciliation and don't benefit from SSR (large payloads, canvas-only rendering).
* Large graph payloads (`/graph`) are fetched once per filter-set and kept in memory; incremental filter changes are applied **client-side** against the cached full/partial graph where possible, rather than re-fetching, to keep interactions snappy. If the backend supports paginated/scoped graph queries (e.g., `/graph?project=Business`), prefer narrow queries by default and let the user opt into "load full graph."
* Optimistic UI is used sparingly — this is a read-heavy, analysis tool, not a CRUD app. The one place optimism matters is SignalR-driven updates (§6), not user mutations.

### 3.4 API Contract Adapter Layer

Because `05-rest-api.md` is a parallel, evolving document, all network calls funnel through `lib/api-client.ts` + per-resource adapter functions in `lib/adapters/*.ts` that map raw API JSON → the Dashboard's internal TypeScript types (`types/graph.ts`, `types/service.ts`, etc.). This gives one place to absorb backend contract changes without touching components, and lets Phase 1/early-Phase-2 UI work proceed against local JSON fixtures (`fixtures/*.json`) with a `USE_FIXTURES=true` env flag swapping the adapter's data source.

### 3.5 Real-Time Updates (summary; full detail in §6)

A dedicated `lib/signalr-client.ts` wraps `@microsoft/signalr`, exposed via a `SignalRProvider` context and a `useArchitectureUpdates()` hook that dispatches incoming hub events into TanStack Query cache updates (`queryClient.setQueryData` / `invalidateQueries`), plus a small Zustand store (`stores/live-status.ts`) tracking connection state (`connected | reconnecting | disconnected`) and a rolling log of recent events for the "live" indicator/toast.

---

## 4. View-by-View Design

### 4.1 Repository Explorer

**Purpose.** Orient a new or returning user in the shape of the solution — the entry point equivalent of opening a solution in an IDE, but architecture-first rather than file-first.

**Key components.**
* `<RepoTree />` — recursive tree component grouped by top-level architectural category (Projects / Business / Infrastructure / API / Tests), each node expandable/collapsible, showing project name, type badge (Domain/Application/Infrastructure/API/Test), and a small count badge (classes, dependencies).
* `<TreeSearchBar />` — client-side fuzzy filter over the already-fetched tree (no server round-trip per keystroke).
* `<ProjectQuickPeek />` — hover/click side panel summarizing a selected project (namespace count, key classes, direct dependencies) with a "View in Graph" / "View in Service Explorer" action.

**Data requirements.** `GET /projects` (full project list with category, references, and summary counts). Fetched once via `useProjects()`, cached under `queryKeys.projects.all`, `staleTime` generous (5 min) since project structure changes rarely relative to session length.

**Interaction design.** Standard disclosure tree (click chevron to expand, click label to select and open Quick Peek). Keyboard navigable (arrow keys, type-ahead). Selecting a node deep-links to `/graph/[nodeId]` or `/services/[serviceId]` depending on node type, preserving the tree's expand state in the URL query string (`?expanded=Business.Domain,Business.Application`) so back-navigation restores context.

**Visualization choice.** Plain DOM tree (no graph library needed) — this view is hierarchical containment, not a network of relationships, and a simple virtualized tree (e.g., `@tanstack/react-virtual` for large trees) is far cheaper and more accessible than a graph canvas.

### 4.2 Dependency Graph

**Purpose.** The flagship visualization — see the whole (or a filtered slice of the) architecture as a network: how projects, services, interfaces, and entities relate.

**Key components.**
* `<DependencyGraphCanvas />` — the graph rendering surface (library choice below).
* `<GraphToolbar />` — zoom controls, layout switcher (force-directed / hierarchical / grid), "fit to view", filter panel toggle, search box.
* `<GraphFilterPanel />` — filter by node type (project/service/interface/entity), by relationship type (references/calls/implements/inherits/injects/uses/publishes/consumes/owns/contains), by project category.
* `<GraphSearchBox />` — typeahead search that pans/zooms to and highlights a matched node.
* `<NodeDetailDrawer />` — slide-over panel with node metadata and quick actions (Open in Service Explorer, Run Impact Analysis, Export Mermaid subgraph) when a node is clicked.
* `<ExportMermaidButton />` — calls `POST /diagram` with current filter/selection scope, opens a modal with the returned Mermaid source (copy button + rendered preview via a Mermaid renderer).

**Data requirements.** `GET /graph` (optionally scoped by query params — project, category, depth). Response modeled as `{ nodes: GraphNode[], edges: GraphEdge[] }`. Expand-node interactions call a scoped fetch (e.g., `GET /graph?expand=nodeId&depth=1`) merged into the existing in-memory graph rather than replacing it, so the user's current pan/zoom and already-expanded neighborhood are preserved. Mermaid export is `POST /diagram` with a body describing scope (whole graph / selected subgraph / single project).

**Interaction design.**
* Zoom/pan via the graph library's native camera controls (mouse wheel + drag; pinch on trackpad/touch).
* Filter panel changes re-render from the already-fetched node/edge set (client-side filter, not re-fetch) for instant feedback; only "expand node" triggers network activity.
* Search highlights and centers a node; non-matching nodes dim rather than disappear, preserving spatial context.
* Node click opens `NodeDetailDrawer`; double-click (or an explicit "Expand" button in the drawer) fetches and merges that node's neighborhood.
* Edge hover shows a tooltip naming the relationship type; edges are color/style-coded by relationship kind (solid = references/calls, dashed = implements/inherits, dotted = injects/uses) with a legend.

**Library choice — Cytoscape.js over React Flow, with Sigma.js as the scale escape hatch.**

React Flow is designed for node-link diagrams where node *count* is modest (dozens to low hundreds) and layout is often manually authored or hierarchical (flowcharts, pipelines) — this is exactly the fit for the Repository Explorer's hierarchical spirit or a small Service Explorer subgraph, but *not* for a whole-solution dependency graph that can realistically reach thousands of classes/interfaces once "entities" are included. For the Dependency Graph specifically:

* **Cytoscape.js** is chosen as the default renderer. It has mature layout algorithms out of the box (`cose`, `dagre`, `breadthfirst`, `concentric`) suited to dependency graphs, first-class support for the filter/expand/collapse interaction pattern (compound nodes for grouping by project/category), good-enough performance into the low thousands of nodes with canvas rendering, and a large plugin ecosystem (e.g., `cytoscape-expand-collapse`, `cytoscape-navigator` for a minimap).
* **Sigma.js** (WebGL-based, built on `graphology`) is the designated upgrade path *specifically for this view* if real-world graphs exceed roughly 3,000–5,000 visible nodes and Cytoscape's canvas renderer starts to drop frames on pan/zoom. Because both consume the same `{ nodes, edges }` shape from `GET /graph`, the plan is to build `<DependencyGraphCanvas />` behind a small renderer-agnostic interface (`GraphRenderer`) from day one, so swapping the implementation later doesn't touch the surrounding toolbar/filter/drawer components. See §11 for the decision checkpoint and load-testing plan.
* **React Flow** is deliberately *not* used for this view (reserved for Service Explorer, §4.3, and small Impact Analysis subgraphs, §4.4, where a curated, small, often tree-like layout is the norm).

### 4.3 Service Explorer

**Purpose.** Answer "if I touch this service, what do I need to know?" — a focused, detail-oriented complement to the whole-graph view.

**Key components.**
* `<ServiceSearchSelect />` — searchable combobox to pick a service (also reachable via deep link from Repository Explorer / Dependency Graph node click).
* `<ServiceSummaryCard />` — name, project, namespace, interfaces implemented.
* `<ServiceRelationsTabs />` — tabbed (or stacked, mobile-friendly) sections: **Dependencies**, **Callers**, **Implementations**, **Interfaces**, **Tests** — each a list with links that navigate to that item's own Service Explorer page or open it in the Dependency Graph.
* `<MiniDependencyGraph />` — a small, curated React Flow diagram rendering just this service, its direct dependencies, and its direct callers (one hop each direction) — a focused visual complement to the tabbed lists.

**Data requirements.** `GET /services/:id` returning dependencies, callers, implementations, interfaces, and associated tests for the selected service. `GET /services` (list) backs the search-select, likely with debounced query params for server-side search once the service count is large.

**Interaction design.** Select or deep-link into a service → summary + tabs render immediately from cached data if the user arrived via a graph node click (pre-warmed query); tabs are lazy — only the active tab's list is rendered, others mount on first activation. The mini-graph supports the same click-to-navigate pattern as the main Dependency Graph but has no independent filter panel (it's intentionally minimal). A persistent "Open full graph centered here" button bridges to §4.2 pre-focused on this node (`/graph/[nodeId]`).

**Library choice — React Flow.** This view's graph is always small (one service + one hop of neighbors, typically single-digit to low-double-digit node counts) and benefits from React Flow's strengths: easy custom node components (styled cards showing type/name/badges matching the design system), simple auto-layout (`dagre` via `elkjs`/`dagre` integration) for a clean top-to-bottom or left-to-right dependency chain, and tight React component integration (no imperative canvas API to bridge) since these nodes need rich, styled content rather than raw circles/labels.

### 4.4 Impact Analysis

**Purpose.** The most decision-relevant view for a developer about to make a change: "what breaks (or should be updated) if I touch this class?"

**Key components.**
* `<ImpactTargetPicker />` — search/select a class, interface, or entity (shared search component with Service Explorer's select, generalized to all symbol kinds).
* `<ImpactSummaryList />` — the affected-components checklist rendering exactly like the README's example (`✓ API`, `✓ Repository`, `✓ Validators`, `✓ Tests`, `✓ Background Workers`), grouped by architectural layer/category, each item expandable to the specific affected classes/files.
* `<ImpactGraphHighlight />` — renders the affected subgraph (reusing `<DependencyGraphCanvas />` or a scoped React Flow diagram — see below) with the target node emphasized and affected nodes highlighted/pulsing outward, unaffected neighbors dimmed.
* `<ImpactSeverityBadge />` — Phase 3 addition once risk scoring is available from the planner/metrics endpoints; shows a rough risk indicator even outside the full AI Planner flow.
* `<RunPlannerFromImpactButton />` (Phase 3) — pre-fills the AI Planner input with "Implement changes to `<target>`" as a bridge between the two views.

**Data requirements.** `GET /impact?target=<id>` returning the affected component list plus (ideally) the subgraph needed to render `<ImpactGraphHighlight />` (node/edge list scoped to the blast radius). If the API returns only IDs, the client resolves display data by merging with the already-cached `/graph` query where possible, falling back to targeted `/services/:id` or `/projects/:id` lookups.

**Interaction design.** Picker at the top; on selection, the summary list renders immediately (fast, textual, matches the README example almost verbatim) while the graph highlight loads/animates in — the textual list must never block on graph rendering since it's the higher-value, faster-to-scan artifact. Each category in the summary list is clickable to filter the graph highlight down to just that category's affected nodes. A "Copy as checklist" action (plain text / Markdown) supports pasting the impact summary into a PR description or ticket.

**Library choice — reuse Cytoscape (Dependency Graph) for large blast radii, React Flow for small ones, chosen at render time by node count.** Impact Analysis blast radius is unpredictable — changing a leaf DTO might affect 3 nodes, changing a core domain entity (like the README's `ModelVersion` example) might affect dozens across five layers. Rather than commit to one library, `<ImpactGraphHighlight />` picks its renderer using the same `GraphRenderer` abstraction from §4.2: if the returned subgraph is small (below ~30 nodes, a reasonable threshold to tune after real usage data), render with React Flow for crisp custom styling of the highlight/dim states and layered layout (grouping by affected category as swimlanes); above that threshold, delegate to the Cytoscape-based `<DependencyGraphCanvas />` pre-filtered to the impacted node set, so large blast radii get the same pan/zoom/search affordances as the main graph instead of an unusable dense React Flow tangle.

### 4.5 Architecture Timeline (Phase 3)

**Purpose.** Show how the architecture evolves scan over scan — growth, churn, and notable structural changes (new projects, removed interfaces) — building historical awareness the way a changelog does for code.

**Key components.**
* `<TimelineFeed />` — reverse-chronological list of scan snapshots, each entry rendering the README-style delta card ("Today — 2,350 classes… Changes: +28 classes, +3 projects, -1 interface").
* `<TimelineTrendChart />` — a line/area chart (class count, project count, interface count over time) using the project's charting approach (see Design System, §5, for chart conventions) to contextualize a single day's delta against the longer trend.
* `<SnapshotDiffDrawer />` — click a delta entry to see the actual added/removed/modified symbols (list, filterable by kind), with links into Service Explorer/Dependency Graph for anything still present.
* `<LiveUpdateBadge />` — a "new changes since you loaded this page" indicator fed by the SignalR hub (§6), letting the user refresh the feed without losing scroll position.

**Data requirements.** `GET /metrics` (or a dedicated snapshot/timeline endpoint if `05-rest-api.md` defines one — flagged as an open question in §11) returning a time-ordered series of `{ timestamp, classCount, projectCount, interfaceCount, deltas: {...} }`. Snapshot diff detail may require a follow-up call (`GET /metrics/:snapshotId/diff` or similar) if the list endpoint only returns summary counts.

**Interaction design.** Infinite-scroll/paginated feed (most users care about "recently"); a date-range picker narrows the trend chart. New live deltas from SignalR prepend to the feed as a distinctly-styled "just happened" entry rather than being silently merged in, so the user notices real-time activity.

**Library choice.** Not a node-link graph at all — this view is a feed + a time-series chart, so neither React Flow nor Cytoscape/Sigma applies. The trend chart uses the same lightweight charting library selected for Coupling Heatmap's supporting stats (see §5) — the platform should standardize on one (e.g., `recharts` or `visx`) rather than introduce a second charting dependency.

### 4.6 Coupling Heatmap (Phase 3)

**Purpose.** Give architects a fast, visual read on structural health — which projects are stable versus dangerously entangled — matching the README's green/yellow/red framing.

**Key components.**
* `<CouplingGrid />` — the primary layout: either a treemap (size = project size/class count, color = coupling score) or a matrix/grid of project cards, each colored per the coupling color scale (§5.4). Treemap is preferred over a plain grid because it lets project *size* carry information alongside coupling color.
* `<CouplingLegend />` — the green/stable, yellow/moderate, red/highly-coupled key, plus the underlying numeric thresholds on hover (transparency into "why is this yellow").
* `<CouplingDetailPanel />` — click a project to see its specific afferent/efferent coupling numbers, its most-coupled neighbors, and a "View in Graph" bridge to the Dependency Graph filtered to that project's immediate neighborhood.
* `<CircularDependencyBanner />` — a dismissible alert surfacing any detected circular dependencies (from the roadmap's "circular dependency detection"), listing the cycle chain with a direct link to view it highlighted in the Dependency Graph.

**Data requirements.** `GET /metrics` (coupling scores per project, likely `{ projectId, afferentCoupling, efferentCoupling, instability, score }`) plus circular-dependency findings (either part of `/metrics` or a dedicated field/endpoint — flagged in §11).

**Interaction design.** Hover for exact numbers (the color band alone is intentionally coarse — three buckets — precision lives in the tooltip/detail panel). Click drills into `<CouplingDetailPanel />`. A toggle switches between treemap and sortable-table views for users who prefer scanning a ranked list over a spatial layout (accessibility and personal preference both matter here).

**Library choice.** A treemap component from the standardized charting library (`recharts`/`visx`/`nivo` treemap) — not React Flow or Cytoscape, since this is a size+color encoding over a flat project list, not a network of relationships. The "View in Graph" bridge is the connective tissue back to the node-link views.

### 4.7 AI Planner (Phase 3)

**Purpose.** The most "AI-first" surface in the dashboard — turn a plain-English feature description into a structured, architecturally-aware implementation plan, mirroring what the MCP Server's `implementation_plan()` gives to AI agents, but for a human in the browser.

**Key components.**
* `<PlannerPromptInput />` — a prominent text input/textarea ("Implement Archive Model") with a submit action and a small set of example prompts for first-time users.
* `<PlannerResultPanel />` — structured rendering of the response: **Affected projects** (chips linking to Repository Explorer/Graph), **New files** (grouped by project, file-tree style), **Modified services** (linking to Service Explorer), **Database changes** (schema/migration summary), **Tests required** (checklist), **Risk level** (badge: low/medium/high, color-matched to the coupling scale for visual consistency), **Estimated effort** (e.g., story points or a time range).
* `<PlannerHistoryList />` — a lightweight sidebar/drawer of recent prompts + results for the session (and, once auth lands in Phase 4, persisted per-user).
* `<PlannerLoadingState />` — since this call may take several seconds (LLM-backed), a purposeful loading state showing plan-generation stages if the API streams progress, or a simple skeleton/spinner with elapsed-time indicator if it's a single blocking call.

**Data requirements.** `POST /implementation-plan` with `{ prompt: string, repoId?/scope? }`, returning the structured plan described above. If the backend supports streaming (SSE or chunked), the client should render incrementally section-by-section rather than waiting for the full payload — flagged as a backend-coordination item in §11 since it materially affects perceived responsiveness.

**Interaction design.** Single input, single primary action, results replace/append below (append preferred, so a user can compare two prompts side by side or scroll back). Every entity mentioned in the result (a project, a service, a file path under an existing project) is a link into the relevant view, turning the plan into a navigable map of the upcoming work rather than static text. A "Run Impact Analysis on affected classes" cross-link ties back to §4.4 for anything the plan flags as modifying an existing class.

**Library choice.** No graph library — this is a form + structured document rendering. Any graph-shaped output *within* a plan (e.g., a small "how these new pieces connect" diagram) reuses the small-scale React Flow renderer from Service Explorer/Impact Analysis for consistency, gated behind the same node-count heuristic.

---

## 5. Design System

### 5.1 Tailwind Conventions

* Tailwind CSS v4 (or latest stable at implementation time) with a project-level `tailwind.config.ts` defining the design tokens below rather than sprinkling raw hex/arbitrary values through components.
* Utility-first in components; anything reused three or more times is extracted into a component in `components/ui/` (buttons, badges, cards, tabs, drawers) rather than a Tailwind `@apply` macro, keeping styling colocated with markup and easy to reason about per the project's component library approach.
* Class name composition uses `clsx`/`tailwind-merge` (a small `cn()` helper in `lib/cn.ts`) to safely merge conditional classes without specificity bugs.
* Layout conventions: CSS grid for page shells (sidebar + main), flexbox within components; consistent spacing scale (Tailwind defaults, no custom spacing tokens unless a specific need arises).

### 5.2 Shared Component Library

`components/ui/` holds framework-agnostic primitives (Button, Badge, Card, Tabs, Drawer/Sheet, Combobox/SearchSelect, Tooltip, Toast, Skeleton) — either hand-built or based on `shadcn/ui` (Radix primitives + Tailwind), which is the recommended starting point given it fits the stack (Next.js + Tailwind + TypeScript) and avoids a heavy component-library dependency while still providing accessible primitives (focus management, keyboar navigation) that graph-heavy, drawer-heavy UIs need.

`components/graph/` holds the visualization-layer shared pieces: `GraphRenderer` abstraction (§4.2), `<DependencyGraphCanvas />`, `<MiniDependencyGraph />`, legends, node/edge style constants, and the shared color-by-relationship-type mapping so every view (Dependency Graph, Service Explorer, Impact Analysis) draws edges consistently.

`components/domain/` holds architecture-domain components that are more than pure UI (e.g., `<ImpactSummaryList />`, `<CouplingGrid />`, `<TimelineFeed />`) — these know about the shape of architecture data, unlike `components/ui/`.

### 5.3 Dark/Light Theming

* Theme is implemented via a `class`-based Tailwind dark mode strategy (`darkMode: 'class'`), toggled by a `<ThemeProvider />` (e.g., `next-themes`) storing preference in `localStorage` and respecting `prefers-color-scheme` by default.
* All design tokens (colors, including the coupling heatmap scale and node/edge colors) are defined as CSS variables in `globals.css` under `:root` and `.dark`, referenced from Tailwind via `theme.extend.colors` pointing at `var(--token-name)`, so graph-canvas code (which can't always consume Tailwind classes directly, e.g., Cytoscape/Sigma styling is JS-config-driven) reads the same variables at runtime via `getComputedStyle`.
* Graph canvases explicitly re-theme on toggle: Cytoscape/Sigma/React Flow don't auto-inherit CSS variables the way DOM does, so a `useGraphTheme()` hook re-reads the relevant CSS variables and calls each library's style-update API (`cy.style()...update()` for Cytoscape, comparable APIs for Sigma/React Flow) whenever the theme changes.

### 5.4 Coupling Heatmap Color Scale

A fixed three-band scale matching the README's green/yellow/red framing, but implemented with a continuous underlying scale so borderline projects aren't jarringly binned:

* **Green (Stable)** — coupling score in the low range (e.g., instability metric below a low threshold, tunable server-side or client-side constant, e.g., `< 0.3`). Token: `--coupling-stable` (light: a mid-saturation green; dark: a slightly desaturated/darkened variant for contrast against a dark background).
* **Yellow (Moderate)** — mid-range score (`0.3–0.6`). Token: `--coupling-moderate`.
* **Red (Highly coupled)** — high score (`> 0.6`). Token: `--coupling-high`.
* Thresholds live in a single constants file (`lib/constants/coupling-scale.ts`) so both the heatmap grid and any inline badges (e.g., a coupling badge on a Service Explorer summary card) agree, and so the exact cutoffs can be tuned once real metrics data is observed without a design-system-wide change.
* Accessibility: color is never the only signal — each band also gets a label ("Stable" / "Moderate" / "Highly coupled") in the legend and in tooltips, and the treemap/grid cells include the label on hover/focus, not just color, satisfying WCAG's "don't rely on color alone" guidance for colorblind users.

---

## 6. Real-Time Updates Integration

### 6.1 SignalR Client Wiring

* `@microsoft/signalr` client wrapped in `lib/signalr-client.ts`, establishing a connection to the hub URL (`NEXT_PUBLIC_SIGNALR_URL`) with automatic reconnect (`withAutomaticReconnect()`), negotiated transport fallback (WebSockets → Server-Sent Events → Long Polling) for environments where WebSockets are blocked.
* Connection lifecycle is owned by a top-level `<SignalRProvider />` mounted once in the dashboard layout (Phase 3 onward), exposing connection state and a subscribe API via React context — individual views don't manage their own hub connections.
* Hub event names are assumed to mirror the resources they affect (exact names to confirm against the backend's hub definition, but planned around): `graph.nodeAdded`, `graph.nodeRemoved`, `graph.nodeUpdated`, `graph.edgeAdded`, `graph.edgeRemoved`, `metrics.updated`, `scan.completed`. Each has a typed payload interface in `types/signalr-events.ts`.

```ts
// lib/signalr-client.ts (sketch)
import * as signalR from '@microsoft/signalr';

export function createArchitectureHubConnection(url: string, accessTokenFactory?: () => Promise<string>) {
  return new signalR.HubConnectionBuilder()
    .withUrl(url, { accessTokenFactory })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();
}
```

### 6.2 Reconciliation with TanStack Query Cache

The core challenge: a graph the user is actively panning/zooming/filtering must update with new data *without* resetting camera position, collapsing expanded nodes, or losing the current selection. The approach:

* Hub handlers never call `invalidateQueries` blindly for graph-shaped data (that would trigger a full re-fetch and re-render, discarding layout state). Instead, they use `queryClient.setQueryData(queryKeys.graph.all, (old) => mergePatch(old, event))` where `mergePatch` applies an additive/subtractive patch (add/remove/update a single node or edge) to the existing cached graph object.
* The `<DependencyGraphCanvas />` component (Cytoscape-backed) listens for graph query-data changes via `useQuery`'s returned data reference and applies **incremental** Cytoscape mutations (`cy.add(...)`, `cy.remove(...)`, `element.data(...)` updates) rather than tearing down and re-initializing the Cytoscape instance — this is the actual mechanism that preserves pan/zoom/selection, and is why the merge-patch step above matters (Cytoscape needs a diff, not a full replacement, to avoid a visible "jump").
* Non-graph resources (`/metrics`, `/services/:id`, `/projects`) use standard `invalidateQueries` on their respective keys on relevant hub events, since those views re-render cheaply and don't have camera/layout state worth preserving.
* A small "N updates available — Refresh" affordance is used instead of silent invalidation for the Timeline feed specifically (§4.5), since prepending live items silently could be surprising in a feed context; the Dependency Graph, by contrast, merges silently plus a subtle non-blocking toast ("3 nodes updated"), since spatial continuity there is more valuable than an explicit refresh gesture.
* Optimistic UI is scoped to exactly one interaction: submitting the AI Planner prompt (§4.7) shows an optimistic "queued" state immediately on submit, reconciled against the actual mutation result/error — everything else in the dashboard is read-only against server-computed data, so there's little else to optimistically predict.

### 6.3 Connection State UX

A small status indicator (dot + label: Live / Reconnecting / Offline) lives in the dashboard top bar. On disconnect, cached data remains visible (stale-but-usable) with a banner noting updates have paused; on reconnect, the client performs one full `invalidateQueries` pass (rather than trying to replay missed events) to guarantee consistency, accepting the one-time layout "jump" as the cost of correctness after a real network gap.

---

## 7. Authentication Integration (Phase 4)

* **Better Auth** is the auth framework, mounted via the Next.js route handler at `app/api/auth/[...all]/route.ts`, with **GitHub OAuth** and **Microsoft Entra ID** as configured providers (matching the two identity sources named in the README — GitHub for open-source/individual use, Entra ID for organizational/enterprise use).
* Session state is read via Better Auth's React hooks/server helpers; the `(dashboard)` layout becomes a protected route group — unauthenticated users are redirected to `(auth)/sign-in`.
* The API client (`lib/api-client.ts`) attaches the session token to REST calls (`Authorization: Bearer ...`) and to the SignalR connection's `accessTokenFactory`, so both the REST API and the hub can authorize per-user/per-repo access once multi-repo (§2 Phase 4) is live.
* Multi-repo authorization: once a user can belong to a team with access to specific repos, the `useRepo()` hook (§3.1) validates the active repo against the session's authorized repo list before rendering repo-scoped views, redirecting to a repo picker otherwise.
* Team collaboration features (sharing a deep link, commenting) are gated behind auth entirely — anonymous/local use (Phase 2–3) has no concept of "who," so these features literally cannot exist before Phase 4's identity layer lands, which is why they're correctly sequenced last.
* No auth work, token handling, or protected routes should be built before Phase 4 — Phase 2–3 dashboard runs as a single implicit workspace with no login, keeping early phases simple and matching the roadmap's explicit phase boundary.

---

## 8. Project/Module Structure

Recommended as a **standalone repository** (`architecture-intelligence-dashboard`) rather than folded into the same repo as the scanner/API/MCP server — the stacks are fully disjoint (TypeScript/Next.js vs. C#/.NET), release cadence differs, and Vercel-style deployment expects the dashboard at the repo root or a clean subpath. If a monorepo is preferred instead, place it at `dashboard/` alongside `api/`, `scanner/`, `mcp-server/` siblings with its own `package.json`/independent deploy pipeline — either way, the internal folder layout below is unaffected.

```
architecture-intelligence-dashboard/
├── app/                             # App Router routes (see §3.1)
│   ├── (dashboard)/...
│   ├── (auth)/...                   # Phase 4
│   ├── api/auth/[...all]/route.ts   # Phase 4
│   ├── layout.tsx
│   ├── providers.tsx                # QueryClientProvider, ThemeProvider, SignalRProvider
│   └── globals.css                  # design tokens / CSS variables
├── components/
│   ├── ui/                          # shared primitives (Button, Card, Tabs, Drawer, ...)
│   ├── graph/                       # GraphRenderer abstraction, canvas wrappers, legends
│   ├── domain/                      # ImpactSummaryList, CouplingGrid, TimelineFeed, PlannerResultPanel...
│   └── layout/                      # Sidebar, TopBar, RepoSwitcher (P4), ThemeToggle
├── hooks/
│   ├── useProjects.ts
│   ├── useServices.ts
│   ├── useDependencyGraph.ts
│   ├── useImpactAnalysis.ts
│   ├── useMetrics.ts
│   ├── useImplementationPlan.ts     # mutation hook
│   ├── useArchitectureUpdates.ts    # SignalR subscription hook
│   └── useRepo.ts                   # active repo context (P4-ready from P2)
├── lib/
│   ├── api-client.ts                # typed fetch wrapper
│   ├── adapters/                    # raw API JSON -> internal types
│   │   ├── graph.adapter.ts
│   │   ├── services.adapter.ts
│   │   └── metrics.adapter.ts
│   ├── query-keys.ts
│   ├── signalr-client.ts
│   ├── cn.ts                        # Tailwind class-merge helper
│   └── constants/
│       ├── coupling-scale.ts
│       └── relationship-styles.ts   # edge color/style per relationship type
├── stores/
│   └── live-status.ts               # Zustand: SignalR connection state + recent events
├── types/
│   ├── graph.ts
│   ├── service.ts
│   ├── project.ts
│   ├── impact.ts
│   ├── metrics.ts
│   ├── planner.ts
│   └── signalr-events.ts
├── fixtures/                        # sample scanner/API output for offline dev (Phase 1 prep)
│   └── graph.sample.json
├── tests/
│   ├── unit/                        # component tests
│   └── e2e/                         # Playwright specs (see §10)
├── public/
├── tailwind.config.ts
├── next.config.ts
├── playwright.config.ts
├── package.json
└── tsconfig.json
```

---

## 9. Deployment Strategy

**Recommendation: Vercel for Phase 2–3; revisit Azure Static Web Apps if/when the org standardizes on Azure for Phase 4's team/enterprise features.**

| Consideration | Vercel | Azure Static Web Apps |
|---|---|---|
| Next.js App Router support | First-party, zero-config, matches framework author | Supported via the Next.js hybrid adapter, generally a step behind Vercel's support for newest App Router features |
| Preview deployments per PR | Built-in, effectively free, core workflow feature | Supported via GitHub Actions integration, more config required |
| Cold start / edge functions | Mature edge runtime, good fit for lightweight API-proxy routes if needed | Improving, historically less mature for Next.js SSR functions |
| Alignment with rest of platform | Platform's API/scanner are Azure-oriented (App Service listed as an API deploy option) — Vercel is a slight ecosystem mismatch but isolates the dashboard's deploy lifecycle from the backend's | Natural fit if the org is already all-in on Azure (shared resource group, Entra ID integration for Phase 4 auth is simpler in-network) |
| Cost at small scale | Generous free/hobby tier, pay-as-you-grow | Free tier exists but Azure billing/portal overhead is heavier for a small static+SSR app |
| Auth (Phase 4) integration | Better Auth works framework-agnostically; GitHub OAuth trivial on any host; Entra ID slightly more natural when the app itself is Azure-hosted (same-tenant app registration, simpler redirect URI management) | Slight edge for Entra ID specifically, given same-cloud app registration |

**Decision:** start on Vercel for Phase 2–3 to maximize iteration speed (preview URLs, zero-config CI/CD, no infra to manage) while the views and data contracts are still stabilizing. Re-evaluate before Phase 4: if the organization is deploying the REST API on Azure App Service and wants a single-cloud story for Entra ID + team/collaboration features, migrating the dashboard to Azure Static Web Apps at that point is a contained effort (Next.js code doesn't change; only the deployment config and DNS/redirect URI updates do). Environment variables (`NEXT_PUBLIC_API_URL`, `NEXT_PUBLIC_SIGNALR_URL`, auth provider secrets) are kept host-agnostic (standard `.env`/platform env-var mechanisms) specifically so this migration stays low-risk.

CI/CD: GitHub Actions (or Vercel's native Git integration) running lint, typecheck, unit tests, and a Playwright smoke suite against a preview deployment before promoting to production, gated the same way regardless of eventual host.

---

## 10. Testing Strategy

### 10.1 Component/Unit Tests

* **Vitest** + **React Testing Library** for component tests, colocated or under `tests/unit/`, covering: `components/ui/*` (primitives render and handle interaction correctly), `components/domain/*` (e.g., `<ImpactSummaryList />` renders the correct grouped checklist from a given API fixture, `<CouplingGrid />` buckets scores into the correct color band per §5.4 thresholds), and hooks (`useDependencyGraph` merges expand-node responses correctly, `useArchitectureUpdates` applies hub patches correctly against a mock cache — this is the highest-value unit test in the whole app given how much correctness hinges on the merge-patch logic in §6.2).
* Adapters (`lib/adapters/*`) get focused unit tests validating raw-API-JSON → internal-type mapping, including malformed/partial payload handling, since this is the seam most exposed to backend contract drift.
* Mock Service Worker (MSW) intercepts REST calls in component tests so tests exercise the real `api-client`/adapter/hook chain against controlled fixture responses rather than mocking hooks directly.

### 10.2 Playwright E2E — Graph Interactions

Given the graph views are the highest-risk, highest-value, and most interaction-heavy surfaces, Playwright specs specifically target:

* **Dependency Graph**: load → verify node count matches fixture → zoom in/out (verify camera state changes) → apply a type filter (verify dimmed/hidden nodes) → search for a known node (verify it's centered/highlighted) → click a node (verify `NodeDetailDrawer` opens with correct data) → click "Expand" (verify new nodes/edges appear, mocked via MSW to return a canned neighborhood).
* **Impact Analysis**: select a known target (e.g., a fixture entity analogous to `ModelVersion`) → verify the affected-category checklist matches the fixture's expected categories exactly (regression-guards the README's headline example) → verify the graph highlight renders and dims non-affected nodes.
* **Service Explorer**: navigate from a Dependency Graph node click → verify the correct service loads → verify each tab (Dependencies/Callers/Implementations/Tests/Interfaces) renders fixture-correct content.
* **Mermaid Export**: trigger export from Dependency Graph and Impact Analysis → verify the modal shows non-empty Mermaid source and a rendered preview.
* **SignalR reconciliation (Phase 3)**: a spec that opens the Dependency Graph, drives the mock hub (via a test-only WebSocket server or MSW's WebSocket interception) to emit a `graph.nodeAdded` event, and asserts the new node appears **without** the page reloading or losing a previously-set zoom level — this directly tests the hardest correctness guarantee in §6.2.
* **AI Planner (Phase 3)**: submit a canned prompt against a mocked `/implementation-plan` response → verify every section (affected projects, new files, modified services, DB changes, tests, risk, effort) renders and that entity links navigate correctly.
* Visual regression is deliberately **not** pursued for the graph canvases at Phase 2/3 (canvas-rendered graphs are notoriously flaky for pixel-diff testing due to layout algorithm non-determinism); instead, correctness is asserted via the DOM/accessibility tree (node labels, ARIA attributes) and via the underlying data model (query cache state), not screenshots.

### 10.3 Test Data

All fixtures used in unit and e2e tests live in `fixtures/` and are the same fixtures used for Phase-1-era fixture-mode development (§3.4), keeping one canonical "sample architecture" (a small fictitious solution with 3–4 projects, ~30 classes, a couple of interfaces, one circular dependency, a range of coupling scores) that exercises every view meaningfully without needing a live backend.

---

## 11. Risks & Open Questions

* **Large graph rendering performance is the single biggest technical risk.** Cytoscape.js canvas rendering is known to degrade past roughly 2,000–5,000 simultaneously-rendered elements depending on layout complexity and interaction (continuous pan/zoom redraw cost). Real repositories with thousands of classes plus entities could exceed this quickly if the Dependency Graph tries to render everything at once. Mitigations planned: default to a scoped/collapsed view (top-level projects only, expand-on-demand) rather than rendering the full graph eagerly; load-test with a synthetic graph at 1k/5k/10k/20k nodes before Phase 2 sign-off; keep the `GraphRenderer` abstraction (§4.2) so swapping to Sigma.js's WebGL renderer is a contained change, not a rewrite, if Cytoscape proves insufficient. **Open action item:** run this load test early in Phase 2 (not deferred to "later") since it affects the filter/expand UX design, not just a performance tuning pass.
* **Cytoscape vs. Sigma vs. React Flow at scale is not fully resolvable until real data exists.** This plan's choice (Cytoscape default, Sigma escape hatch, React Flow for small/curated views) is a reasoned starting point, not a settled decision — it should be revisited with actual node/edge counts from a real scanned solution once Phase 1's scanner produces representative output, ideally before investing heavily in Cytoscape-specific interaction code (expand/collapse plugin, compound nodes) that wouldn't port cleanly to Sigma.
* **Backend contract gaps.** Several assumed endpoints/fields are not yet confirmed in `05-rest-api.md` as of this writing: a dedicated timeline/snapshot-history endpoint for §4.5 (vs. overloading `/metrics`), a circular-dependency field/endpoint for §4.6, whether `/impact` returns a ready-to-render subgraph or just IDs (materially affects §4.4's implementation cost), and whether `POST /implementation-plan` supports streaming (materially affects §4.7's perceived latency). These should be resolved via direct coordination with the API doc owner before the corresponding Phase 2/3 work starts, not discovered mid-sprint.
* **SignalR hub event schema is assumed, not confirmed.** The event names and payloads in §6.1 are a reasonable guess based on the resources they'd affect; the actual hub contract needs to be finalized alongside the REST contract, and the merge-patch logic (§6.2) is only as good as the granularity the hub actually provides (e.g., if the hub only emits a coarse `graph.changed` with no delta detail, the client would be forced into full re-fetch/invalidation, defeating the pan/zoom-preserving design — this would be a meaningful regression worth pushing back on during hub design).
* **Multi-repo (Phase 4) retrofitting risk.** Although this plan designs routes/hooks (`useRepo()`, deep-linkable URLs) to make the future `/[repoId]/` segment a low-friction addition, the actual query-key structure (`queryKeys.projects.all` etc., §3.2) does **not** yet namespace by repo — this must be revisited before Phase 4 starts so cached data from different repos can't collide; flagged now so it isn't forgotten as "already handled" just because routing was made repo-ready.
* **Mermaid export fidelity.** `POST /diagram`'s Mermaid output needs to actually render cleanly in standard Mermaid renderers (GitHub, GitLab, docs tools) for the export feature to be useful outside the dashboard — this is primarily a backend concern but the dashboard's preview-and-copy UX (§4.2) should validate the returned Mermaid source client-side (attempt a render, surface an error state) rather than assume it's always valid.
* **Charting library choice is left open** (`recharts` vs `visx` vs `nivo` for §4.5/§4.6) — recommend deciding at the start of Phase 3 based on team familiarity and bundle-size tolerance rather than pre-committing now, since neither the Timeline nor Coupling Heatmap is being built in Phase 2.

---

## 12. Task Breakdown

### Phase 1 (prep only, backend-owned phase)

- [ ] Confirm dashboard repo strategy (standalone vs. monorepo subfolder) with the team
- [ ] Scaffold Next.js app (App Router, TypeScript, Tailwind) with no real data wiring
- [ ] Set up base tooling: ESLint, Prettier, Vitest, Playwright config (empty smoke test only)
- [ ] Generate and commit `fixtures/graph.sample.json` from an early `arch scan`/`arch graph` output
- [ ] Review and sign off on `05-rest-api.md`'s draft shapes for `/projects`, `/services`, `/graph`, `/impact`, `/metrics`

### Phase 2 (core build)

- [ ] App shell: root layout, `Providers` (QueryClient, ThemeProvider), navigation sidebar/top bar
- [ ] `lib/api-client.ts`, `lib/query-keys.ts`, adapters for projects/services/graph/impact
- [ ] Design system foundation: Tailwind tokens, `components/ui/*` primitives (via shadcn/ui or hand-built), dark/light theme toggle
- [ ] Repository Explorer: `<RepoTree />`, search/filter, Quick Peek panel, deep links
- [ ] Dependency Graph: `GraphRenderer` abstraction, Cytoscape-backed `<DependencyGraphCanvas />`, toolbar, filter panel, search, node detail drawer, expand-node merge logic
- [ ] Load-test Dependency Graph against synthetic 1k/5k/10k-node fixtures; document findings against §11's risk
- [ ] Service Explorer: search/select, summary card, relation tabs, React-Flow-based mini graph
- [ ] Impact Analysis (basic): target picker, affected-category checklist, graph highlight (size-based renderer choice per §4.4)
- [ ] Mermaid export: modal + preview, wired to `POST /diagram`, from Dependency Graph and Impact Analysis
- [ ] Playwright e2e: Dependency Graph interactions, Impact Analysis checklist, Service Explorer navigation, Mermaid export
- [ ] Deploy to Vercel with preview-per-PR CI

### Phase 3 (live + intelligent)

- [ ] `lib/signalr-client.ts`, `<SignalRProvider />`, connection-state store, live indicator UI
- [ ] Reconciliation: merge-patch logic for graph cache updates, incremental Cytoscape mutation path, invalidation path for non-graph resources
- [ ] Architecture Timeline: feed, trend chart, snapshot diff drawer, live-update badge
- [ ] Coupling Heatmap: treemap/grid, legend, detail panel, circular-dependency banner, color-scale constants (§5.4)
- [ ] AI Planner: prompt input, structured result panel, history list, loading/streaming states, mutation hook
- [ ] Cross-links: Impact Analysis → AI Planner, Coupling Heatmap → Dependency Graph, Timeline diff → Service/Graph views
- [ ] Playwright e2e: SignalR reconciliation (no camera reset), AI Planner full render, Coupling Heatmap color-banding correctness
- [ ] Finalize charting library choice and apply consistently across Timeline and Coupling Heatmap

### Phase 4 (collaboration & scale)

- [ ] Better Auth integration: route handler, sign-in page, GitHub OAuth, Microsoft Entra ID
- [ ] Protect `(dashboard)` route group behind session; sign-out flow
- [ ] Namespace TanStack Query keys by `repoId`; introduce `/[repoId]/` route segment and `<RepoSwitcher />`
- [ ] Multi-repo authorization checks in `useRepo()`
- [ ] Team collaboration: shareable deep links (already URL-addressable from Phase 2 design), node/service commenting, presence indicators
- [ ] Historical snapshot browsing: time-travel picker over Architecture Timeline data, graph-as-of-date rendering
- [ ] Architecture quality scoring view/widget
- [ ] Re-evaluate Vercel vs. Azure Static Web Apps deployment decision against finalized auth/infra requirements
- [ ] Playwright e2e: auth flows, repo switching, snapshot time-travel
