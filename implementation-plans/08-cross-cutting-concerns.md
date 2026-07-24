# 08 — Cross-Cutting Concerns: Configuration, Authentication, Deployment & Platform Setup

> Companion documents: `01-scanner.md`, `02-graph-store.md`, `03-cli.md`, `04-mcp-server.md`, `05-rest-api.md`, `06-dashboard.md`, `07-ai-planner.md` (parallel plans, referenced but not owned here).

---

## 1. Overview & Purpose of This Document

The Architecture Intelligence Platform is composed of several independently developed components: the .NET Scanner, the CLI, the MCP Server, the REST API, the SignalR live-update channel, and the Next.js Dashboard. Each of those components has (or will have) its own implementation plan describing internal design.

Several concerns, however, do not belong to any single component. They are shared infrastructure that every component depends on, and if each component team (or each future contributor) invents its own answer, the platform fragments:

* **Configuration** — the `arch.config.yaml` file is read by the Scanner, the CLI, the REST API, and indirectly by the MCP Server and Dashboard (via the API). If its schema, versioning, and validation rules are not owned centrally, every consumer will drift.
* **Authentication** — human users (via the Dashboard) and machine clients (CLI, MCP Server, CI jobs) both need to authenticate against the same API. Getting this wrong once Phase 4 multi-tenant/team features land is expensive to retrofit.
* **Repository/solution layout** — the project spans a .NET solution, a Next.js app, an MCP server package, and shared schemas. Without an explicit monorepo convention decided up front, build tooling, CI, and dependency management become inconsistent.
* **CI/CD and distribution** — the Scanner ships as *two* artifacts (a .NET global tool and an npm wrapper) that must stay version-aligned; the API ships as a container image; the Dashboard ships as a static/edge deployment. These pipelines interact (e.g., a schema change must bump the config version consumed by all three).
* **Deployment topology** — the platform intentionally evolves from "local CLI tool, zero deployment" (Phase 1) to "multi-tenant cloud SaaS" (Phase 4). Each phase has different infrastructure needs, and decisions made in Phase 1 (e.g., how config is loaded) must not block Phase 4 (e.g., per-tenant config, hosted secrets).
* **Observability** — logging/tracing conventions must be consistent across a Roslyn-based .NET scanner process, an ASP.NET Core API, and a Next.js frontend, or diagnosing a cross-component issue (e.g., "why did the graph update not reach the dashboard") becomes guesswork.

This document is the single source of truth for these cross-cutting concerns. Component-specific plans (01, 03, 05, 06, etc.) should **reference** this document rather than redefine configuration schemas, auth flows, or deployment topology themselves. Where a component plan needs something not covered here, that is a signal to extend this document, not to make a local decision.

**Scope boundary:** this document owns the configuration *schema*, not the scanner's config-consuming logic (owned by doc 01) or the CLI's config commands (owned by doc 03). It owns the shared *auth architecture* (token model, session model, client types), not the REST API's endpoint-level authorization logic (doc 05) or the Dashboard's login UI (doc 06).

---

## 2. Repository & Solution Structure

### 2.1 Decision: single monorepo

The platform will live in a single monorepo (this repository, `Arch`) rather than split across multiple repos. Rationale:

* The Scanner, CLI, MCP Server, API, and Dashboard evolve together and frequently need atomic cross-component changes (e.g., a new graph node type touches the Scanner, the API DTOs, and the Dashboard's TypeScript types in one PR).
* The configuration schema (Section 3) must be shared verbatim between the .NET side (Scanner/CLI/API) and any Node-based tooling (npm wrapper, Dashboard). A monorepo lets us generate/share types from one canonical schema.
* Phase 1 has a single active contributor set; multi-repo overhead (cross-repo versioning, submodules) is not justified until the project has multiple independent teams — a Phase 4 concern at the earliest, and even then a monorepo with path-scoped CI is usually simpler to operate than polyrepo for a platform this tightly coupled.

### 2.2 Top-level layout

```text
arch-intelligence-platform/
├── .github/
│   └── workflows/
│       ├── ci-dotnet.yml
│       ├── ci-dashboard.yml
│       ├── ci-mcp-server.yml
│       ├── release-dotnet-tool.yml
│       ├── release-npm-scanner.yml
│       ├── release-mcp-server.yml
│       ├── release-api-image.yml
│       └── release-dashboard.yml
├── docs/
│   ├── README.md                          # (existing vision doc)
│   └── implementation-plans/
│       ├── 01-scanner.md
│       ├── 02-graph-store.md
│       ├── 03-cli.md
│       ├── 04-mcp-server.md
│       ├── 05-rest-api.md
│       ├── 06-dashboard.md
│       ├── 07-ai-planner.md
│       └── 08-cross-cutting-concerns.md   # (this document)
├── schemas/
│   └── config/
│       ├── v1/
│       │   └── arch.config.schema.json    # canonical JSON Schema, versioned by directory
│       └── CHANGELOG.md
├── src/
│   ├── dotnet/
│   │   ├── PatternVision.sln              # the .NET solution (scanner, CLI, API, domain)
│   │   ├── Directory.Build.props          # shared MSBuild props (nullable, langversion, analyzers)
│   │   ├── Directory.Packages.props       # central package version management (CPM)
│   │   ├── Common/
│   │   │   └── PatternVision.Common/                    # shared kernel: Result<T>, errors, primitives
│   │   ├── Config/
│   │   │   └── PatternVision.Configuration/             # config POCOs, YAML loader, validator, binds to schemas/config
│   │   ├── Scanner/
│   │   │   ├── PatternVision.Scanner.Core/               # Roslyn/MSBuild Workspace scanning engine
│   │   │   ├── PatternVision.Scanner.Rules/              # DI/MediatR/inheritance rule detectors
│   │   │   └── PatternVision.Scanner.Tests/
│   │   ├── Graph/
│   │   │   ├── PatternVision.Graph.Abstractions/         # node/edge model
│   │   │   ├── PatternVision.Graph.Storage.Sqlite/
│   │   │   ├── PatternVision.Graph.Storage.Postgres/     # Phase 2+
│   │   │   └── PatternVision.Graph.Tests/
│   │   ├── Cli/
│   │   │   ├── PatternVision.Cli/                        # `arch` global tool entry point
│   │   │   └── PatternVision.Cli.Tests/
│   │   ├── Api/
│   │   │   ├── PatternVision.Api/                        # ASP.NET Core Minimal API host
│   │   │   ├── PatternVision.Api.Contracts/              # DTOs shared with MCP server / OpenAPI
│   │   │   ├── PatternVision.Api.Auth/                   # Better Auth bridge, API-key auth, JWT validation
│   │   │   └── PatternVision.Api.Tests/
│   │   └── Mcp/
│   │       └── PatternVision.McpServer.Dotnet/           # optional in-process MCP transport (Phase 1-2)
│   ├── mcp-server/                        # standalone Node/TS MCP server (Phase 2+), talks to REST API
│   │   ├── package.json
│   │   ├── src/
│   │   └── tests/
│   ├── scanner-npm/                       # npm wrapper package that shells out to / bundles the .NET tool
│   │   ├── package.json
│   │   ├── bin/arch.js
│   │   └── postinstall/                   # platform-specific binary download logic
│   └── dashboard/                         # Next.js application
│       ├── package.json
│       ├── app/
│       ├── components/
│       ├── lib/
│       │   └── config-schema/             # generated TS types from schemas/config/v1/*.json
│       └── tests/
├── packages/
│   └── config-schema-types/               # shared TS package: types + zod validator generated from JSON Schema
│       ├── package.json
│       └── src/index.ts
├── infra/
│   ├── docker/
│   │   ├── api.Dockerfile
│   │   └── docker-compose.local.yml       # local Postgres + API for dev
│   ├── azure/
│   │   ├── app-service.bicep
│   │   └── static-web-app.bicep
│   └── fly/
│       └── fly.toml
├── .editorconfig
├── package.json                            # root workspace (pnpm/turbo) for JS packages
├── pnpm-workspace.yaml
└── global.json                             # pinned .NET SDK version
```

### 2.3 Tooling decisions

| Concern | Decision | Rationale |
|---|---|---|
| .NET solution layout | Single `PatternVision.sln`, one project per bounded concern, `Directory.Build.props` for shared settings | Matches the scanner's own `scanOrder` convention (Common → Domain → Application → Infrastructure → API → Tests) — the platform eats its own dog food |
| .NET package versions | Central Package Management (`Directory.Packages.props`) | Avoids version drift across ~15 projects |
| JS workspace | pnpm workspaces + Turborepo | Three JS packages (dashboard, mcp-server, scanner-npm wrapper, config-schema-types) need shared caching and dependency hoisting |
| Config schema source of truth | `schemas/config/v{n}/arch.config.schema.json`, hand-authored JSON Schema | .NET side generates POCOs/validation from it via `NJsonSchema` or manual binding; JS side generates types via `json-schema-to-typescript` + `zod` |
| Cross-language contract sharing | `PatternVision.Api.Contracts` (C#) and `packages/config-schema-types` (TS) are both generated or kept in lockstep from `schemas/` at build time, never hand-duplicated | Prevents API/dashboard DTO drift |

---

## 3. Configuration Schema Design

### 3.1 Goals

* One canonical schema definition, consumed identically by the Scanner (doc 01), the CLI (doc 03), and the REST API (doc 05) when it accepts remote scan configuration.
* Forward-compatible: adding new optional fields must not break older CLI/Scanner binaries reading a newer config, and vice versa within a major version.
* Explicit versioning so that Phase 3/4 features (multi-repository, quality scoring rules, incremental-watch tuning) can extend the schema without silently breaking Phase 1 configs.
* Environment-variable substitution for secrets/paths without requiring secrets to live in the YAML file itself.

### 3.2 File identity and discovery

* Canonical filename: `arch.config.yaml` (matches CLI's `arch init` output), located at repository root next to `solution`.
* CLI/Scanner resolve config in this order (first match wins):
  1. `--config <path>` CLI flag
  2. `ARCH_CONFIG_PATH` environment variable
  3. `./arch.config.yaml` in the current working directory
  4. Walk up parent directories (like `.gitignore` resolution) up to the filesystem root or a `.git` boundary
* If none found, `arch init` is suggested; commands that require config fail with a clear error rather than silently defaulting.

### 3.3 `configVersion` and versioning strategy

Every config file must declare a top-level `configVersion` (integer, schema major version). This is **new** relative to the README's example — the example in the README is treated as `configVersion: 1` with the field implicit/defaulted for backward compatibility, but all files generated by tooling going forward include it explicitly.

Versioning rules:

* `configVersion` is a single integer representing a **major** schema version. Breaking changes (removing a field, changing a field's type or meaning, changing required-ness in a way that invalidates old files) bump this integer.
* Additive, backward-compatible changes (new optional field, new enum value) do **not** bump `configVersion`; they are tracked in `schemas/config/CHANGELOG.md` under the current version.
* Schema files live at `schemas/config/v{N}/arch.config.schema.json`. The loader picks the schema matching the file's declared `configVersion` (defaulting to `1` when absent, with a deprecation warning starting in Phase 2).
* When a new major version ships, the previous version's schema file is retained (not deleted) for at least two minor platform releases, and the CLI ships a `arch config migrate` command (Phase 2+) that rewrites an old config to the new shape where migration is mechanical.
* No config version will be removed from support without a documented deprecation window of at least one full roadmap phase.

### 3.4 Defaults, overrides, and precedence

Resolved configuration is computed by layering, lowest to highest precedence:

1. **Built-in defaults** — hardcoded in `PatternVision.Configuration` (e.g., `ignore: [bin, obj, node_modules, .git]`, `languages: [csharp]`, all `rules.*` default to `true`).
2. **File config** — `arch.config.yaml` values override defaults for any key present.
3. **Environment variable substitution** — values inside the YAML may reference `${ENV_VAR_NAME}` or `${ENV_VAR_NAME:-default}` syntax (a restricted subset of shell parameter expansion). These are resolved after YAML parsing, before validation, so a substituted value must still satisfy the schema. This is intended for things like connection strings or tokens that must not be committed (`storage.connectionString: "${ARCH_DB_CONNECTION}"`), not for structural config like `scanOrder`.
4. **CLI flag overrides** — e.g., `arch scan --ignore node_modules,dist` or `arch scan --set rules.followMediatR=false` override the file value for that invocation only; they are never written back to the file automatically.
5. **API request-scoped overrides** (Phase 2+, when the REST API accepts remote scan triggers) — the API may accept a JSON body with a subset of config keys to override for a single scan job; these follow the same schema and are merged the same way, with the same validation applied server-side.

Precedence is: defaults < file < env-substituted values (in place) < CLI flags < API request overrides. Merge is a deep merge for objects (`rules`, future nested sections) and full replacement for arrays (`scanOrder`, `ignore`, `languages`) — partial array merging is explicitly out of scope to avoid ambiguity about append-vs-replace semantics.

### 3.5 Validation approach

* **Structural validation**: JSON Schema (Draft 2020-12) is the canonical validation source. The YAML is parsed to an in-memory document model (e.g., `YamlDotNet` on the .NET side) then converted to JSON for schema validation, so the same schema file validates configs regardless of producing language.
* **.NET side**: `PatternVision.Configuration` uses `YamlDotNet` to parse, converts to `System.Text.Json.Nodes.JsonNode`, validates against the embedded schema via `Json.Schema` (JsonSchema.Net), then binds to strongly typed `ArchConfig` POCOs. Validation errors are surfaced with YAML line/column info where possible (round-tripped from `YamlDotNet`'s parsing events).
* **Node side** (npm scanner wrapper, MCP server, dashboard's "upload custom config" UI in Phase 2+): `ajv` validates against the same JSON Schema file (vendored from `schemas/config/v{N}/`), and `packages/config-schema-types` exports generated TypeScript types plus a `zod` schema derived from the same source for ergonomic client-side validation.
* **Semantic validation** (beyond JSON Schema's structural capability), performed in code after schema validation passes:
  * `solution` path must exist relative to the config file's directory (checked by Scanner/CLI at scan time, not by the schema itself — the schema only constrains it to be a non-empty string ending in `.sln`).
  * `scanOrder` entries should correspond to actual top-level folders/projects found under the solution; unknown entries produce a warning, not a hard error (repository layouts vary).
  * Duplicate entries in `scanOrder` or `ignore` are rejected.
* **CLI command**: `arch config validate` (doc 03) runs this full pipeline and prints human-readable diagnostics; the REST API exposes the equivalent as `POST /config/validate` (doc 05) for the dashboard's config editor (Phase 2+ "Architecture Explorer" settings panel).

### 3.6 JSON Schema definition (v1)

This is the canonical schema for `configVersion: 1`, stored at `schemas/config/v1/arch.config.schema.json`:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://arch-intelligence.dev/schemas/config/v1/arch.config.schema.json",
  "title": "Architecture Intelligence Platform Configuration",
  "description": "Schema for arch.config.yaml, version 1.",
  "type": "object",
  "additionalProperties": false,
  "required": ["solution", "scanOrder", "languages"],
  "properties": {
    "configVersion": {
      "type": "integer",
      "const": 1,
      "default": 1,
      "description": "Major schema version. Absent is treated as 1 for backward compatibility with pre-versioning configs."
    },
    "solution": {
      "type": "string",
      "minLength": 1,
      "pattern": "\\.(sln)$",
      "description": "Path (relative to the config file) to the .NET solution file to scan."
    },
    "scanOrder": {
      "type": "array",
      "items": { "type": "string", "minLength": 1 },
      "minItems": 1,
      "uniqueItems": true,
      "description": "Ordered list of top-level project groups/folders that determines scan and dependency-layering order."
    },
    "ignore": {
      "type": "array",
      "items": { "type": "string", "minLength": 1 },
      "uniqueItems": true,
      "default": ["bin", "obj", "node_modules", ".git"],
      "description": "Glob-like path segments/patterns excluded from scanning."
    },
    "languages": {
      "type": "array",
      "items": {
        "type": "string",
        "enum": ["csharp", "typescript", "javascript"]
      },
      "minItems": 1,
      "uniqueItems": true,
      "default": ["csharp"],
      "description": "Languages the scanner should parse. Only 'csharp' is implemented in Phase 1; others are reserved for the multi-language roadmap goal."
    },
    "rules": {
      "type": "object",
      "additionalProperties": false,
      "default": {},
      "properties": {
        "followInheritance": { "type": "boolean", "default": true },
        "followDI": { "type": "boolean", "default": true },
        "followMediatR": { "type": "boolean", "default": true },
        "followProjectReferences": { "type": "boolean", "default": true }
      },
      "description": "Toggles for which relationship-detection rules the scanner applies."
    },
    "storage": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "provider": {
          "type": "string",
          "enum": ["sqlite", "postgres", "neo4j"],
          "default": "sqlite"
        },
        "connectionString": {
          "type": "string",
          "description": "Supports ${ENV_VAR} substitution. Never commit literal secrets here."
        }
      },
      "description": "Graph store backend configuration. Optional in Phase 1 (sqlite default, local file)."
    },
    "watch": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "debounceMs": { "type": "integer", "minimum": 0, "default": 500 },
        "notify": { "type": "boolean", "default": true }
      },
      "description": "Incremental watcher tuning (Phase 3)."
    },
    "multiRepo": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "repositories": {
          "type": "array",
          "items": {
            "type": "object",
            "required": ["name", "path"],
            "properties": {
              "name": { "type": "string", "minLength": 1 },
              "path": { "type": "string", "minLength": 1 }
            }
          }
        }
      },
      "description": "Multi-repository support (Phase 4). Reserved; not read by Phase 1-3 tooling."
    }
  }
}
```

Notes on the schema:

* `additionalProperties: false` at the root and in `rules`/`storage`/`watch` is intentional so typos (`scanOder`) fail loudly instead of being silently ignored — this is a common source of "why isn't my ignore list working" bugs.
* `multiRepo` is defined now (Phase 4 feature) so that the schema's evolution is additive rather than requiring a `configVersion: 2` bump purely to add a field that was foreseeable from the roadmap. Fields reserved for future phases are documented as "reserved" so early tooling can validate against them without implementing behavior yet.
* `storage.connectionString` is explicitly documented as accepting env substitution and is called out in Section 4 as never being committed in plaintext.

### 3.7 Example fully-resolved config (Phase 1)

```yaml
configVersion: 1
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
storage:
  provider: sqlite
  connectionString: "Data Source=./.arch/graph.db"
```

Example using env-var substitution for a Postgres-backed Phase 2 deployment:

```yaml
configVersion: 1
solution: PatternVision.sln
scanOrder: [Common, Domain, Application, Infrastructure, API, Tests]
languages: [csharp]
storage:
  provider: postgres
  connectionString: "${ARCH_DB_CONNECTION}"
```

---

## 4. Environments & Secrets Management

### 4.1 Environment tiers

| Environment | Purpose | Config source | Secret source |
|---|---|---|---|
| **Local dev** | Individual developer machine running scanner/CLI/API/dashboard against SQLite | `arch.config.yaml` in repo + `.env.local` (gitignored) | `.env.local` files, never committed; `dotnet user-secrets` for API-side local secrets |
| **CI** | GitHub Actions running build/test/lint | `arch.config.yaml` (test fixtures) | GitHub Actions **Encrypted Secrets** (repo or environment-scoped) |
| **Staging** | Pre-production instance of API + Dashboard, Phase 2+ | Environment-specific `arch.config.yaml` fragment or fully env-var-driven storage config | Cloud provider secret store (Azure Key Vault / Fly secrets / Railway variables), injected as process env vars |
| **Production** | Live multi-tenant deployment, Phase 4 | Same mechanism as staging, stricter access control, per-tenant overrides stored server-side (not in a shared YAML file) | Same as staging, with rotation policy (Section 5.6) |

### 4.2 Principles

* **Secrets never live in `arch.config.yaml` literally.** The schema (Section 3.6) only allows `${ENV_VAR}` references for secret-shaped fields (`storage.connectionString`). This keeps the config file safe to commit to source control, which matters because the Scanner's own config is itself scanned/versioned alongside the code it describes.
* **`.env` files are local-only and gitignored.** A `.env.example` (committed) documents every variable a component needs without values.
* **CI secrets are scoped per workflow/environment** using GitHub Environments (`staging`, `production`) so that a PR from a fork cannot exfiltrate production secrets — deployment workflows that need production secrets require the `production` GitHub Environment's required reviewers gate.
* **Cloud secrets are never baked into container images.** The API's `Dockerfile` (Section 6, `infra/docker/api.Dockerfile`) takes zero secrets at build time; all secrets are injected as environment variables at container start (Azure App Service application settings, Fly.io secrets, Railway variables, or Docker Compose `env_file` locally).

### 4.3 Inventory of secrets by phase

| Secret | Introduced | Consumers | Local dev source | Cloud source |
|---|---|---|---|---|
| SQLite file path | Phase 1 | Scanner, CLI | Not a secret (local file) | N/A |
| Postgres connection string | Phase 2 | API, Scanner (remote scans) | `.env.local` | Azure Key Vault ref / Fly secret / Railway var |
| OpenAI API key | Phase 2 (doc search embeddings), Phase 3 (AI planner) | API (embedding + planner calls) | `.env.local` | Key Vault / platform secret store |
| GitHub OAuth client id/secret | Phase 4 | API (Better Auth provider), Dashboard (redirect config) | `.env.local`, GitHub OAuth App registered as "development" | Separate OAuth App registration per environment (dev/staging/prod), secret in Key Vault |
| Microsoft Entra ID app registration (client id/secret, tenant id) | Phase 4 | API (Better Auth provider) | Dev tenant app registration | Key Vault, per-environment app registration |
| Better Auth session secret (`BETTER_AUTH_SECRET`) | Phase 4 | API | `.env.local` (random dev value) | Key Vault, rotated per Section 5.6 |
| Machine-client API keys / client-credentials secrets | Phase 4 | MCP Server, CLI (remote mode), CI jobs | `.env.local` scoped to a "local" client | Issued per organization via Dashboard admin UI, stored hashed server-side |
| pgvector-hosting Postgres credentials | Phase 2 | API (embeddings) | Local Postgres via `docker-compose.local.yml` | Managed Postgres (Azure Database for PostgreSQL, Neon, etc.) |
| Neo4j credentials (optional) | Phase 3+ (if adopted) | Graph store adapter | Local Neo4j Docker container | Managed Neo4j Aura or self-hosted, Key Vault |

### 4.4 Configuration-vs-secrets boundary in code

* `PatternVision.Configuration` (the schema-bound POCOs) never itself resolves secrets from a cloud vault — it only performs `${ENV_VAR}` substitution against `Environment.GetEnvironmentVariable`. Populating those environment variables from Azure Key Vault, Fly secrets, etc. is an infrastructure/deployment concern (Section 7), keeping the config loader simple and testable.
* The API's own startup composition (`Program.cs`, doc 05) is responsible for binding `IConfiguration` (ASP.NET Core's standard configuration system: `appsettings.json` → `appsettings.{Environment}.json` → environment variables → Key Vault provider in Phase 4) separately from the `arch.config.yaml` scanning configuration. These are **two distinct configuration systems** by design:
  * `arch.config.yaml` — describes *what to scan and how* (repository-facing, versioned schema).
  * ASP.NET Core `IConfiguration` / Next.js env vars — describes *how this deployment of the platform itself is wired* (connection strings, OAuth secrets, feature flags). This is standard 12-factor app configuration and does not need a custom JSON Schema.

---

## 5. Authentication & Authorization Architecture

This section defines the shared architecture; endpoint-level authorization rules belong to doc 05 (REST API) and UI-level session handling belongs to doc 06 (Dashboard). Authentication is a **Phase 4** roadmap item ("Cloud synchronization", "Team collaboration", "Multi-repository support" imply real user identity); Phases 1-3 run unauthenticated/local-only as described in Section 7. This section is written now so that Phase 1-3 API surface (doc 05) is designed to not preclude it later (e.g., always accepting a bearer token slot even if unchecked initially).

### 5.1 Two distinct principal types

The platform has two fundamentally different kinds of callers, and the auth architecture treats them as separate first-class concepts rather than forcing both through one login flow:

1. **Human users** — developers, tech leads, architects, EMs using the Dashboard (and, indirectly, the REST API the dashboard calls) via a browser session.
2. **Machine clients** — the CLI (`arch` running in a developer's terminal or in CI), the MCP Server (running as a subprocess of Claude Code/Codex/Cursor, or as a hosted service in Phase 4), and any future automation (e.g., a nightly scan job).

Conflating these (e.g., asking the CLI to do an interactive OAuth browser flow every invocation) produces poor ergonomics; conflating them the other way (long-lived static tokens for dashboard users) is a security anti-pattern. Hence two flows sharing one identity/authorization backend.

### 5.2 Human authentication: Better Auth

* **Provider**: [Better Auth](https://www.better-auth.com/) is adopted as the auth library on the API side (it is TypeScript-native; since the API is ASP.NET Core, Better Auth runs as a small companion Node service — or, if by Phase 4 a Better Auth-compatible .NET implementation/port is unavailable, the API implements an equivalent OAuth2/OIDC-relying-party flow directly using `Microsoft.AspNetCore.Authentication.OAuth` and Entra's `Microsoft.Identity.Web`, exposing an API-compatible session model). This decision point is flagged as an open question in Section 10 — the README's own tech stack choice (Better Auth, a JS library) needs a concrete bridge into a C# backend, and the two candidate approaches are:
  * **Option A (Better Auth as a sidecar)**: a small Node/Express (or Next.js API route, co-located in `src/dashboard`) service owns the Better Auth instance, issues its own signed session cookies/JWTs, and the ASP.NET Core API validates those JWTs (JWKS-based, standard OIDC validation) without needing to know about GitHub/Entra directly.
  * **Option B (native ASP.NET Core OIDC)**: skip Better Auth, use `Microsoft.Identity.Web` for Entra ID and `AspNet.Security.OAuth.GitHub` for GitHub OAuth directly in the API, issuing its own session.
  * **Recommendation**: Option A, because it keeps the Dashboard (Next.js) and its auth provider in the same runtime/language, matches the README's explicit tech choice, and lets the ASP.NET Core API be a pure OIDC/JWT *relying party* — a well-trodden, low-risk integration (`AddJwtBearer` + JWKS endpoint) regardless of which identity provider issued the token.
* **Identity providers wired through Better Auth**:
  * GitHub OAuth — primary for individual developers/OSS-style usage; also useful later for linking a user's GitHub identity to repository permissions (Phase 4 multi-repo).
  * Microsoft Entra ID — primary for organizational/enterprise usage (SSO), especially relevant since the target audience (architects, EMs) commonly sits inside Entra-managed tenants.
* **Session model**:
  * Better Auth issues a session (cookie-based, `httpOnly`, `secure`, `sameSite=lax`) to the browser after OAuth callback completes.
  * For calls from the Next.js frontend to the ASP.NET Core API, the Better Auth sidecar mints a short-lived **JWT access token** (5–15 minute expiry) bound to the user's session, which the browser/Next.js server sends as `Authorization: Bearer <jwt>` to the API. Refresh happens transparently via the Better Auth session (silent refresh from an httpOnly refresh cookie), never exposing a long-lived token to client-side JS.
  * The API validates the JWT against the sidecar's published JWKS endpoint (`/.well-known/jwks.json`), checking `iss`, `aud`, `exp`, and standard claims — no shared-secret coupling between the two runtimes.

### 5.3 Machine-client authentication: API keys and OAuth client-credentials

Machine clients cannot do an interactive browser OAuth redirect. Two mechanisms, chosen per use case:

1. **Personal/organization API keys** (simplest, default for CLI and MCP Server in most setups):
   * A human user, authenticated via Section 5.2, generates a scoped API key from the Dashboard ("Settings → API Keys"). The key is shown once, stored server-side only as a salted hash (never reversible), with metadata: name, scopes, created-by, last-used, expiry (optional).
   * The CLI (`arch login --api-key` or `ARCH_API_KEY` env var) and the MCP Server (`MCP_ARCH_API_KEY`) send it as `Authorization: ApiKey <key>` (a distinct scheme from `Bearer` so the API can apply different rate limits/logging).
   * Scopes are coarse-grained in Phase 4's initial cut: `read:graph`, `write:scan`, `admin:org` — enough to let a CI job have read/write-scan access without admin rights.
2. **OAuth2 client-credentials grant** (for organization-level automation, e.g., a shared CI service account, or a future hosted MCP Server acting on behalf of an org rather than a single user):
   * An admin registers a "client application" (client id + secret) in the Dashboard, scoped to an organization/team.
   * The client exchanges `client_id`/`client_secret` for a short-lived access token at `POST /oauth/token` (standard `grant_type=client_credentials`), following RFC 6749 §4.4.
   * Preferred over static API keys when the caller needs frequently-rotated, audit-friendly tokens (e.g., a shared build agent) rather than a single human-owned key.

Both mechanisms terminate at the same authorization layer as human sessions (Section 5.4) so that endpoint code (doc 05) checks one unified `ClaimsPrincipal` regardless of whether the caller is a browser session, an API key, or a client-credentials token — ASP.NET Core's multi-scheme authentication (`AddAuthentication().AddJwtBearer(...).AddScheme<ApiKeyAuthOptions,...>(...)`) selects the scheme per-request based on the `Authorization` header's prefix.

### 5.4 Authorization model (multi-repo / team scenarios, Phase 4)

* **Tenancy unit**: an **Organization** owns one or more **Repositories** (each repository maps to one scanned architecture graph / `arch.config.yaml`). This directly supports the roadmap's "Multi-repository support" goal.
* **Roles** (organization-scoped, RBAC not ABAC — kept simple deliberately):
  * `owner` — manage billing, members, delete org.
  * `admin` — manage repositories, API keys, OAuth client apps, members below owner.
  * `member` — read/write access to graphs of repositories they're granted, can trigger scans, use AI planner.
  * `viewer` — read-only dashboard access (graph browsing, impact analysis views), cannot trigger scans or mutate config.
* **Repository-level grants**: a member can be granted access to a subset of an org's repositories (not automatically all), supporting larger orgs where not every architect should see every team's graph.
* **Machine clients inherit the creating user's/org's grants**, scoped by the key's declared scopes (Section 5.3) intersected with the granting user's actual repository access — a key can never exceed the permissions of whoever created it.
* **Authorization enforcement point**: centralized in the API (doc 05) via an ASP.NET Core authorization policy/handler that resolves `(principal, repositoryId) → allowed?`, not duplicated per endpoint. The Dashboard (doc 06) additionally hides UI affordances the user lacks permission for, but never relies on UI hiding alone for security.

### 5.5 What Phases 1-3 look like without auth

Before Phase 4, the API and Dashboard run **unauthenticated by default** (single-user local tool, per Section 7). To avoid a disruptive redesign later:

* Doc 05's Minimal API endpoint definitions should route through a single authorization filter/middleware placeholder from day one (`app.MapGet("/projects", ...).RequireAuthorization("RepoRead")` with a permissive Phase 1-3 policy that always succeeds), so that flipping on real checks in Phase 4 is a policy change, not an endpoint rewrite.
* The config schema's reserved `multiRepo` section (Section 3.6) and the graph store's schema should include a nullable `organizationId`/`repositoryId` foreign key from Phase 2 onward (even though Phase 1-3 has exactly one implicit repository), so Phase 4 multi-tenancy is a matter of populating and enforcing an existing column rather than a schema migration touching every table.

### 5.6 Secret rotation & token lifetimes

| Credential | Lifetime | Rotation policy |
|---|---|---|
| Dashboard session (Better Auth cookie) | 30 days sliding | Automatic on activity; revocable from "Settings → Sessions" |
| API access JWT (browser → API) | 15 minutes | Silently refreshed via session |
| Personal API key | No forced expiry by default; optional expiry settable at creation | Owner can revoke anytime; last-used timestamp surfaced to nudge cleanup of stale keys |
| OAuth client-credentials secret | 1 year default | Dashboard prompts rotation before expiry; supports two active secrets during rotation window (avoid downtime) |
| `BETTER_AUTH_SECRET` / JWT signing key | Rotated on a schedule (e.g., every 90 days) or on suspected compromise | JWKS multi-key support so old tokens remain valid until natural expiry during rotation |

---

## 6. CI/CD Pipeline Design

### 6.1 Pipeline inventory

| Pipeline | Trigger | Scope |
|---|---|---|
| `ci-dotnet.yml` | PR/push touching `src/dotnet/**` or `schemas/**` | Restore, build, `dotnet format --verify-no-changes`, analyzers, unit + integration tests, code coverage |
| `ci-dashboard.yml` | PR/push touching `src/dashboard/**` or `packages/config-schema-types/**` | `pnpm install`, `eslint`, `tsc --noEmit`, `vitest`/`jest`, `next build` |
| `ci-mcp-server.yml` | PR/push touching `src/mcp-server/**` | `pnpm install`, lint, type-check, unit tests |
| `release-dotnet-tool.yml` | Tag push `cli-v*` | Build, pack `PatternVision.Cli` as a `dotnet tool` package, push to NuGet |
| `release-npm-scanner.yml` | Tag push `scanner-v*` | Build/bundle native tool refs, `npm publish` for `src/scanner-npm` |
| `release-mcp-server.yml` | Tag push `mcp-v*` | `npm publish` for `src/mcp-server` |
| `release-api-image.yml` | Tag push `api-v*` or merge to `main` (staging auto-deploy) | Build multi-arch Docker image, push to GHCR/ACR, deploy to staging; manual approval gate for production |
| `release-dashboard.yml` | Merge to `main` | Vercel/Azure SWA deploy (preview for PRs, production for `main`) |

### 6.2 `.NET` CI (`ci-dotnet.yml`) — representative definition

```yaml
name: CI - .NET
on:
  pull_request:
    paths:
      - "src/dotnet/**"
      - "schemas/**"
      - ".github/workflows/ci-dotnet.yml"
  push:
    branches: [main]
    paths:
      - "src/dotnet/**"
      - "schemas/**"

jobs:
  build-test:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest]
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - name: Restore
        run: dotnet restore src/dotnet/PatternVision.sln
      - name: Format check
        run: dotnet format src/dotnet/PatternVision.sln --verify-no-changes
      - name: Build
        run: dotnet build src/dotnet/PatternVision.sln --no-restore -c Release -warnaserror
      - name: Test
        run: >
          dotnet test src/dotnet/PatternVision.sln --no-build -c Release
          --collect:"XPlat Code Coverage" --results-directory ./coverage
      - name: Validate config schema fixtures
        run: dotnet run --project src/dotnet/Config/PatternVision.Configuration.Tools -- validate-fixtures schemas/config
      - uses: codecov/codecov-action@v4
        with:
          directory: ./coverage
```

Key points: the Windows leg of the matrix matters specifically because the Scanner uses MSBuild Workspace/Roslyn against real `.sln`/`.csproj` files, and path/casing differences between Windows and Linux have historically caused MSBuild Workspace bugs — testing on both catches those early. The "Validate config schema fixtures" step re-validates a set of committed example configs (including the README's own example, Section 3.7) against the schema so schema regressions are caught in CI, not discovered by a user.

### 6.3 Dashboard CI (`ci-dashboard.yml`) — representative definition

```yaml
name: CI - Dashboard
on:
  pull_request:
    paths: ["src/dashboard/**", "packages/config-schema-types/**"]
  push:
    branches: [main]
    paths: ["src/dashboard/**", "packages/config-schema-types/**"]

jobs:
  build-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: pnpm/action-setup@v4
        with: { version: 9 }
      - uses: actions/setup-node@v4
        with: { node-version: 20, cache: "pnpm" }
      - run: pnpm install --frozen-lockfile
      - run: pnpm --filter config-schema-types build
      - run: pnpm --filter dashboard lint
      - run: pnpm --filter dashboard typecheck
      - run: pnpm --filter dashboard test -- --run
      - run: pnpm --filter dashboard build
```

### 6.4 Versioning & release strategy for the dual-published Scanner

The Scanner ships as **two** artifacts that must reference the same underlying capability set: a **.NET global tool** (`dotnet tool install -g PatternVision.Cli`) and an **npm package** (`npm install -g @patternvision/scanner`). They must stay version-aligned so a bug report referencing "scanner 1.4.2" is unambiguous regardless of install method.

* **Single source of version truth**: a `VERSION` file (or `Directory.Build.props`'s `<Version>` combined with `src/scanner-npm/package.json`'s `version` kept in lockstep by a release script, not by hand) at repo root drives both packages. A `scripts/bump-version.ts` (or simple shell script) updates both `Directory.Build.props` and `package.json` atomically in one commit, tagged `scanner-v{X.Y.Z}`.
* **Semver alignment**: both packages adopt strict semver and release **simultaneously** from the same tag — there is intentionally no independent versioning track for "npm wrapper patch fixes" vs ".NET tool patch fixes," because divergence is exactly the confusion this alignment avoids. If the npm wrapper needs a fix unrelated to the .NET tool (e.g., a `postinstall` download-path bug), it still gets a coordinated patch release of both, even though only the wrapper's code changed — the version number is a paired identity, not two independently evolving numbers.
* **npm package mechanics**: `@patternvision/scanner`'s `postinstall` script downloads the matching platform-specific `dotnet tool` binary (self-contained, trimmed, published for `win-x64`, `linux-x64`, `osx-arm64`, `osx-x64`) from the GitHub Release matching the same tag, rather than requiring the end user to have the .NET SDK installed. This mirrors how tools like `esbuild`/`swc` distribute native binaries via npm.
* **NuGet package mechanics**: `PatternVision.Cli` is packed with `<PackAsTool>true</PackAsTool>` and `<ToolCommandName>arch</ToolCommandName>`, published to nuget.org, installable via `dotnet tool install -g PatternVision.Cli`.
* **Pre-release channel**: `-beta.N` / `-rc.N` suffixes published to a `next` npm dist-tag and NuGet's prerelease flag, for testing before promoting to `latest`.

### 6.5 API container release

```dockerfile
# infra/docker/api.Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/dotnet/ .
RUN dotnet restore Api/PatternVision.Api/PatternVision.Api.csproj
RUN dotnet publish Api/PatternVision.Api/PatternVision.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "PatternVision.Api.dll"]
```

Multi-arch (`linux/amd64`, `linux/arm64`) images are built via `docker buildx` in CI and pushed to GitHub Container Registry (GHCR); Azure App Service, Railway, and Fly.io all pull from GHCR directly (no dual-registry maintenance burden). A `:sha-<commit>` tag is pushed on every `main` merge for traceability, plus a `:vX.Y.Z` tag on release and a floating `:latest`.

### 6.6 Dashboard release

Vercel is the default target (near-zero-config for Next.js, automatic PR preview deployments). Azure Static Web Apps is documented as the alternative path for organizations standardized on Azure (relevant given Entra ID auth and Azure App Service for the API) — `release-dashboard.yml` is written to support either via a workflow input/environment variable (`DEPLOY_TARGET: vercel|azure-swa`) rather than hard-coding one, so a team can switch without a pipeline rewrite.

---

## 7. Deployment Topologies per Phase

### 7.1 Phase 1 — Local-only, no deployment

* Nothing is deployed. `arch` (CLI + embedded scanner + basic MCP server) runs entirely on a developer's machine.
* Graph store is a local SQLite file (`.arch/graph.db`), gitignored.
* The "Basic MCP server" runs as a local stdio-transport subprocess spawned by Claude Code/Cursor, per the standard MCP local-server pattern — no network exposure at all.
* No CI/CD deployment pipelines are active yet beyond `ci-dotnet.yml` build/test; release pipelines (Section 6.4) exist so the CLI/npm package can be distributed, but "deployment" in the infra sense does not apply.
* **Action for this phase**: ensure `PatternVision.Configuration`, the SQLite storage provider, and the CLI have zero hard dependency on network services, so Phase 1 genuinely works offline.

### 7.2 Phase 2 — Single-instance deploy for early users

* Introduces the Next.js Dashboard and the REST API as deployed services for early adopters/testers, but still single-tenant per deployment (one org, no auth yet per Section 5.5).
* **Topology**:
  ```text
  [Vercel / Azure SWA]  Next.js Dashboard  ──HTTPS──▶  [Azure App Service / Fly.io / Railway]  ASP.NET Core API
                                                              │
                                                        Postgres (managed) + pgvector
                                                              │
                                                        (SQLite retained as local-dev fallback)
  ```
* Deployment is a **single instance** of the API (no horizontal scaling needed yet), fronted by the cloud provider's default HTTPS/TLS termination. A managed Postgres instance (Azure Database for PostgreSQL Flexible Server, or Railway/Fly Postgres add-ons) replaces SQLite for any deployed (non-local-CLI) usage, since concurrent dashboard users need a real server-based DB.
* CORS on the API is locked to the dashboard's known origin(s); no public API access beyond the dashboard's own calls (auth is still off, but network exposure is minimized by not documenting/advertising the API as a public integration point yet).
* Self-hosting is explicitly supported via `infra/docker/docker-compose.local.yml`-style compose file for organizations that want to run API + Postgres themselves rather than using a hosted deployment — this doubles as the "simple single-instance deploy" story for early users who prefer on-prem.
* **Action for this phase**: stand up `infra/azure/app-service.bicep` (or equivalent Fly/Railway config) and `infra/azure/static-web-app.bicep`; wire `release-api-image.yml` and `release-dashboard.yml` staging targets.

### 7.3 Phase 3 — SignalR / live-update infra considerations

Phase 3 adds the Incremental Watcher and live graph updates pushed to connected dashboard clients via SignalR (per the README's WebSocket API box in the architecture diagram). This changes deployment requirements because SignalR needs either sticky sessions or a backplane once more than one API instance exists:

* **While still single-instance** (small teams, Phase 3 early adopters): no special infra needed — one process holds all SignalR connections in-memory. This is the default and should remain the recommended path for as long as it's viable (most Phase 3 users are still small/single-team).
* **If horizontal scaling becomes necessary** (a single instance struggles with connection count or the provider auto-scales instances): two supported options, both must be planned for even if not implemented until actually needed:
  1. **Sticky sessions** (session affinity) at the load balancer — supported natively by Azure App Service (`ARR affinity`) and by Fly.io (via `fly-replay`/regional routing) and Railway with some caveats. Simple, no extra Azure spend, but limits scaling elasticity and complicates zero-downtime deploys (in-flight SignalR connections drop on the affinitized instance's restart).
  2. **Azure SignalR Service** (managed backplane) — decouples client connections from the API's own compute, letting the API scale statelessly. Recommended default for Azure App Service deployments once >1 instance is needed; requires switching `AddSignalR()` to `AddSignalR().AddAzureSignalR(...)` in doc 05's `Program.cs`, which is a config-only change if `PatternVision.Api` treats the SignalR hub transport as swappable from day one (Section 7.3's action item below).
  3. For non-Azure deployments (Fly.io/Railway), a Redis-backed SignalR backplane (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`) is the portable equivalent — a small managed Redis instance is cheap on both platforms.
* **Action for this phase**: doc 05 should design the SignalR hub registration so the backplane provider is selected via configuration (`signalR.backplane: none | azure | redis`), not hardcoded, so the topology decision above can be deferred to actual deployment time rather than locked in at code-design time.

### 7.4 Phase 4 — Multi-tenant cloud deployment & scaling

* Full multi-tenant SaaS topology: multiple organizations, each with one or more repositories, isolated at the data layer (row-level `organizationId` scoping, per Section 5.5) rather than one-deployment-per-tenant, to keep operational overhead sane.
* **Topology**:
  ```text
  [Vercel / Azure SWA — Dashboard, multi-tenant aware via subdomain or org-switcher]
             │  HTTPS + JWT (Section 5.2)
             ▼
  [Load Balancer / Front Door] ──▶ [API instances ×N, autoscaled] ──▶ [Azure SignalR Service or Redis backplane]
             │                              │
             │                              ├──▶ Postgres (primary graph metadata + auth + orgs), read replicas as needed
             │                              ├──▶ Neo4j (optional, advanced graph traversal for large orgs)
             │                              └──▶ Postgres+pgvector (doc/semantic search embeddings)
             │
  [Better Auth sidecar] ──OIDC──▶ [GitHub OAuth] / [Microsoft Entra ID]
  ```
* **Scaling considerations**:
  * API instances are stateless (SignalR backplane from 7.3 makes this possible) and scale horizontally behind autoscale rules keyed on CPU/request latency.
  * Graph queries for very large orgs may benefit from Neo4j (the README already lists this as "optional for advanced graph traversal") — this becomes primarily a Phase 4 concern since Phase 1-3's single-repo scale rarely needs a dedicated graph database over well-indexed Postgres adjacency tables.
  * Scan jobs (triggered via CI or manually) are offloaded to a background worker/queue (e.g., Azure Queue Storage + a worker role, or a hosted-job pattern within the API using `IHostedService` with a durable queue) rather than executing synchronously in an API request, since Roslyn-based scans of large solutions can take minutes.
  * Per-tenant resource quotas (max repositories, max scan frequency, storage size) are enforced at the API's authorization/business-logic layer, not at the infra layer, in Phase 4's initial cut — infra-level tenant isolation (e.g., dedicated DB per large enterprise customer) is a documented future option (Section 10), not a Phase 4 requirement.
  * MCP Server may itself become a hosted, multi-tenant service in Phase 4 (rather than purely a local stdio subprocess) to support "AI agent connects to my organization's architecture graph from anywhere" — this requires the MCP Server to authenticate as a machine client (Section 5.3) on behalf of a specific user/org, which is why that mechanism is designed independent of "local CLI only" assumptions.

---

## 8. Distribution Strategy

### 8.1 .NET global tool (NuGet)

* Package id: `PatternVision.Cli`; command name: `arch` (`<ToolCommandName>arch</ToolCommandName>` in the `.csproj`).
* Published to nuget.org on tagged releases (`release-dotnet-tool.yml`, Section 6.4), signed with a NuGet API key stored as a GitHub encrypted secret scoped to the `release` environment (requiring manual approval for production publishes, to prevent an unreviewed workflow change from silently publishing).
* Self-contained/trimmed single-file builds are additionally published as GitHub Release assets (per-RID: `win-x64`, `linux-x64`, `osx-arm64`, `osx-x64`) specifically so the npm wrapper (8.2) can download a binary without requiring the .NET SDK on the end user's machine.
* Installation: `dotnet tool install -g PatternVision.Cli` (requires .NET SDK) or `dotnet tool update -g PatternVision.Cli` for upgrades.

### 8.2 npm wrapper package

* Package name: `@patternvision/scanner` (scoped, to avoid squatting/collision risk on the unscoped `scanner`/`arch` names).
* Strategy: a thin `bin/arch.js` shim that, on `postinstall`, detects the current platform/arch and downloads the matching self-contained binary asset from the GitHub Release matching `package.json`'s version (Section 6.4's paired versioning), caching it under the package's `node_modules/.bin`-adjacent storage. This is the same pattern used by tools like `esbuild`, `swc`, and `turbo` to ship native binaries through npm without bundling every platform's binary in one multi-hundred-MB package.
* Rationale for wrapper-over-rewrite: rewriting the scanner in JS/TS would abandon Roslyn's semantic model (the README's explicit reason for choosing C#/.NET for the scanner) — the npm package exists purely for JS-ecosystem-native installation ergonomics (`npx @patternvision/scanner scan`, `npm install -D @patternvision/scanner` in a Node monorepo that also happens to contain .NET services), not to reimplement scanning logic in Node.
* Installation: `npm install -g @patternvision/scanner` then `arch scan`, or ad hoc via `npx @patternvision/scanner scan`.

### 8.3 MCP server package

* Published to npm as `@patternvision/mcp-server` (Node/TS implementation per Section 5.1's Option A rationale — MCP servers in the ecosystem are conventionally Node/TS, and this lets it be launched via `npx @patternvision/mcp-server` from any MCP-compatible client's config, matching the ecosystem convention (e.g., how other MCP servers are configured in Claude Desktop/Claude Code's `mcp.json`).
* Versioned independently of the Scanner/CLI pairing (Section 6.4) since it has its own release cadence tied to MCP protocol/tool surface changes rather than scanning capability changes — but its `package.json` declares a compatible-API-version range against the REST API (Section 8.4) to catch drift.

### 8.4 API compatibility versioning

* The REST API exposes its own version via `GET /version` and an `X-Api-Version` response header, following semver independent of the CLI/Scanner version (the API's release cadence, driven by container deploys, is decoupled from the CLI's NuGet/npm release cadence).
* The MCP Server and CLI (when operating in "remote" mode against a deployed API, Phase 4) declare a minimum/maximum compatible API version range and fail fast with an actionable error if a deployed API is outside that range, rather than producing confusing runtime errors from a schema mismatch.

### 8.5 Dashboard

* Not "distributed" in the package sense — deployed continuously per Section 6.6/7. No versioned release artifact beyond the deployed build; the git tag/commit SHA visible in the deployed footer is sufficient traceability.

---

## 9. Observability

### 9.1 Approach: OpenTelemetry end-to-end

OpenTelemetry (OTel) is adopted as the single observability standard across the .NET API/Scanner and the Next.js Dashboard, because it is vendor-neutral (works whether the eventual backend is Azure Monitor/Application Insights, Grafana/Tempo/Loki, or a SaaS like Honeycomb) and has first-class support in both ecosystems.

* **.NET side (API, Scanner, CLI)**:
  * `OpenTelemetry.Extensions.Hosting` + `OpenTelemetry.Instrumentation.AspNetCore` + `OpenTelemetry.Instrumentation.Http` wired in `PatternVision.Api`'s `Program.cs` for traces and metrics out of the box (incoming requests, outgoing HTTP calls to OpenAI's API, DB calls via `Npgsql`'s OTel instrumentation).
  * The Scanner (a CLI-invoked batch process, not a long-lived server) emits a single root span per `arch scan` invocation with child spans per major phase (`ParseSolution`, `ResolveReferences`, `BuildGraph`, `PersistGraph`, `DetectRules.DI`, `DetectRules.MediatR`, etc.) so scan performance regressions are diagnosable per-phase, not just as one opaque wall-clock number. Exported via OTLP to whatever collector is configured (or written to a local trace file for `arch scan --trace` in Phase 1 when no collector is configured, so this is useful even offline).
  * Structured logging via `Microsoft.Extensions.Logging` with an OTel logging exporter, using consistent structured fields across components: `traceId`, `repositoryId` (from Phase 2+), `component` (`scanner|api|cli`), `configVersion`.
  * Metrics: request rate/latency/error-rate (RED metrics) for the API automatically via ASP.NET Core instrumentation; custom metrics for scan duration, nodes/edges created per scan, graph size, SignalR connection count (Phase 3+).
* **Next.js Dashboard**:
  * `@opentelemetry/api` + `@vercel/otel` (if deployed on Vercel) or the standard Node OTel SDK (if self-hosted/Azure SWA with a Node runtime) for server-side route handlers and API calls to the backend.
  * Client-side (browser) telemetry is kept minimal and privacy-conscious: no invasive session recording; Web Vitals (already a Next.js/Vercel built-in) cover core UX performance signals, and any custom client events (e.g., "graph render time for N nodes") are sent as simple metrics, not full traces, to avoid over-engineering browser-side observability before it's needed.
* **Correlation across components**: the API propagates `traceparent` (W3C Trace Context) on any outbound calls it makes (OpenAI API, database), and the Dashboard's server-side fetches to the API also propagate `traceparent`, so a single user action (e.g., "run AI planner") is traceable end-to-end from browser → Next.js server → ASP.NET Core API → OpenAI Responses API as one distributed trace once a Phase 3+ collector is in place.

### 9.2 Backend/collector choice per phase

| Phase | Collector/backend |
|---|---|
| 1 | None required; local file/console exporters only (`arch scan --trace` writes a local OTLP JSON file for manual inspection) |
| 2 | Lightweight hosted option: Azure Application Insights (if deploying to Azure App Service, near-zero extra setup) or Grafana Cloud free tier (if on Fly.io/Railway) |
| 3 | Same backend, now also ingesting SignalR connection metrics and scan-phase traces for performance tuning of the incremental watcher |
| 4 | Full production-grade stack: dedicated OTel Collector (self-hosted or managed) fanning out to Azure Monitor/Application Insights and/or a dedicated tracing backend, with per-tenant log/metric tagging (`organizationId`) enabling per-customer support debugging without cross-tenant data leakage |

### 9.3 Logging conventions

* Structured (JSON) logs everywhere, never string-interpolated free text as the primary log line — use `ILogger`'s message templates (`_logger.LogInformation("Scan completed for {RepositoryId} in {DurationMs}ms", repoId, duration)`) so fields are queryable.
* Log levels: `Debug` for per-symbol scanner detail (off by default, opt-in via `arch scan -v`), `Information` for phase completion/API request summaries, `Warning` for recoverable issues (e.g., an unresolved project reference), `Error` for scan/request failures, `Critical` reserved for process-level failures (e.g., DB unreachable at startup).
* No secrets or full source code content ever logged — the scanner's structured metadata (symbol names, project names) is fine to log; raw source text is not.

---

## 10. Risks & Open Questions

1. **Better Auth ↔ ASP.NET Core bridge is unproven.** Section 5.2's "sidecar" approach (Option A) is a reasonable default but adds an extra deployed service (and extra operational surface: two runtimes issuing/validating tokens) purely to honor the README's stated tech choice. **Open question**: before Phase 4 begins, spike both Option A and Option B (native ASP.NET Core OIDC via `Microsoft.Identity.Web` + `AspNet.Security.OAuth.GitHub`, dropping Better Auth) and pick based on actual integration friction, not on the README's tech list alone — the README predates detailed design and should be revisited if Option B proves meaningfully simpler.
2. **Config schema `additionalProperties: false` is strict** — it will reject any config with typos or premature use of not-yet-supported fields, which is intentional (Section 3.6) but means every new optional field requires a schema PR before users can adopt it, even experimentally. **Open question**: consider an `x-` prefix escape hatch (common JSON Schema convention) allowing unvalidated experimental keys (`x-experimentalFeatureFlag`) without loosening the schema generally.
3. **SQLite → Postgres migration path is not fully designed.** Section 7.2 assumes Postgres is introduced wholesale at Phase 2 for deployed instances, but Phase 1 users with an existing local SQLite graph will want a migration/import path when they adopt the Dashboard. **Open question**: needs a dedicated migration tool or `arch export`/`arch import` command — flagged for doc 01/03 to address, cross-referenced here because it is a deployment-transition concern.
4. **Neo4j's role is still "optional."** The README lists it as optional; this document defers a firm decision to when graph traversal performance on Postgres actually becomes a bottleneck (Phase 4, likely only for very large multi-repo orgs). **Risk**: introducing a second graph-capable datastore late adds real migration complexity; **mitigation**: keep the graph storage layer behind `PatternVision.Graph.Abstractions` (already planned in Section 2.2) so swapping/adding a Neo4j-backed implementation doesn't require touching consumers.
5. **SignalR scaling decision (sticky sessions vs. Azure SignalR Service vs. Redis backplane) is deferred to "when needed."** This is intentional (avoid premature infra spend) but carries the risk of a scramble if a single Phase 3 customer suddenly needs multi-instance scale. **Mitigation**: the configuration-driven backplane selection (Section 7.3) is the safeguard — verify doc 05 actually implements it as swappable from the start, not bolted on reactively.
6. **Machine-client API key scoping (`read:graph`, `write:scan`, `admin:org`) is a first draft** and may prove too coarse once real usage patterns emerge (e.g., a CI job that should only be able to trigger scans for one specific repository, not all repos it happens to have access to). **Open question**: whether to add per-repository scoping to API keys in the initial Phase 4 cut or treat it as a fast-follow.
7. **Central Package Management + multi-target npm/NuGet dual publishing adds release-process complexity** (Section 6.4) that a single early contributor must maintain manually until it's worth scripting/automating fully. **Risk**: manual version-bump-in-two-places is error-prone; **mitigation**: prioritize the `scripts/bump-version.ts` automation (Section 6.4) before the first public release, not after.
8. **Multi-tenant data isolation is row-level (`organizationId` scoping) rather than infra-level from the start.** This is the pragmatic default for cost/ops reasons but may not satisfy enterprise customers with strict data-residency/isolation requirements. **Open question**: document this as a known Phase 4 limitation and revisit "dedicated deployment per large customer" only if actual enterprise demand materializes.
9. **OpenAI dependency for embeddings/AI planner introduces a third-party data-handling question**: source code identifiers/architecture metadata sent to OpenAI's API for embeddings (Section 9's `Instrumentation.Http` will trace these calls). **Open question**: for enterprise/Entra ID customers with strict data-handling policies, evaluate whether an Azure OpenAI Service deployment (data stays in the customer's Azure tenant/region) should be a configurable alternative to the public OpenAI API before Phase 4 enterprise onboarding.

---

## 11. Task Breakdown

### Phase 1 — Foundations (local-only)

- [ ] Scaffold monorepo layout (Section 2.2): `src/dotnet` solution skeleton, `Directory.Build.props`, `Directory.Packages.props`, `global.json`.
- [ ] Author `schemas/config/v1/arch.config.schema.json` (Section 3.6) and commit example fixtures (Section 3.7).
- [ ] Implement `PatternVision.Configuration`: YAML parsing (`YamlDotNet`), JSON Schema validation (`JsonSchema.Net`), env-var substitution, POCO binding, precedence/merge logic (Section 3.4).
- [ ] Add `arch config validate` command wiring (coordinate with doc 03).
- [ ] Set up `ci-dotnet.yml` (Windows + Linux matrix, format check, build, test, coverage).
- [ ] Set up `.editorconfig`, analyzer ruleset shared via `Directory.Build.props`.
- [ ] Confirm zero network dependency in Phase 1 code paths (SQLite-only, no Postgres/OpenAI calls reachable without explicit opt-in).
- [ ] Add local file-based OTel trace export for `arch scan --trace` (Section 9.1).
- [ ] Publish first `PatternVision.Cli` NuGet pre-release and `@patternvision/scanner` npm pre-release to validate the dual-distribution pipeline end-to-end (Section 6.4, 8.1, 8.2) even before feature-complete.

### Phase 2 — Dashboard & API stand-up

- [ ] Scaffold `src/dashboard` (Next.js) and `packages/config-schema-types` (generated TS types + zod schema from the JSON Schema).
- [ ] Set up `pnpm-workspace.yaml` + Turborepo config; `ci-dashboard.yml`.
- [ ] Stand up `PatternVision.Api` Minimal API host with placeholder `RequireAuthorization("RepoRead")` permissive policy (Section 5.5) from day one.
- [ ] Add `organizationId`/`repositoryId` nullable columns to graph storage schema now, even though unused until Phase 4 (Section 5.5).
- [ ] Provision managed Postgres option; implement `PatternVision.Graph.Storage.Postgres` alongside existing SQLite provider behind `PatternVision.Graph.Abstractions`.
- [ ] Add `storage.provider`/`storage.connectionString` handling with env-var substitution end-to-end (Section 3.4, 4.4).
- [ ] Write `infra/docker/api.Dockerfile`, `infra/azure/app-service.bicep`, `infra/azure/static-web-app.bicep`.
- [ ] Set up `release-api-image.yml` (multi-arch build, GHCR push, staging auto-deploy) and `release-dashboard.yml` (Vercel/Azure SWA, configurable target).
- [ ] Wire up Application Insights or Grafana Cloud for staging (Section 9.2).
- [ ] Document `.env.example` for both API and Dashboard; add secrets inventory to internal ops runbook.
- [ ] `POST /config/validate` endpoint for dashboard config editor (Section 3.5).

### Phase 3 — Live updates & metrics

- [ ] Implement SignalR hub in `PatternVision.Api` with backplane provider selectable via config (`signalR.backplane: none|azure|redis`) (Section 7.3).
- [ ] Add `watch` section handling to config loader (debounce, notify) per schema (Section 3.6).
- [ ] Load-test single-instance SignalR capacity to establish the threshold at which sticky sessions/backplane actually becomes necessary.
- [ ] Extend OTel instrumentation to cover SignalR connection metrics and per-phase scan traces (Section 9.1).
- [ ] Document sticky-session configuration for Azure App Service / Fly.io / Railway as a fallback runbook, even if Azure SignalR Service/Redis is the primary recommended path.

### Phase 4 — Auth, multi-tenancy, scaling

- [ ] Spike Better Auth sidecar vs. native ASP.NET Core OIDC (Risk #1); make and document the final decision.
- [ ] Implement chosen auth bridge; wire GitHub OAuth and Microsoft Entra ID providers.
- [ ] Implement JWT validation middleware in `PatternVision.Api` (JWKS-based, multi-scheme alongside API-key/client-credentials auth per Section 5.3).
- [ ] Build API key issuance UI in Dashboard (Section 5.3) + hashed storage + scopes.
- [ ] Implement OAuth2 client-credentials grant endpoint (`POST /oauth/token`) for org-level machine clients.
- [ ] Implement RBAC authorization policies (`owner/admin/member/viewer`, repository-level grants) replacing the Phase 2 permissive placeholder policy.
- [ ] Populate and enforce `organizationId`/`repositoryId` scoping across all graph queries and API endpoints.
- [ ] Stand up production secret rotation process (Key Vault, JWKS multi-key rotation) per Section 5.6.
- [ ] Design and implement background scan job queue (offload from synchronous API requests) for Phase 4 scale.
- [ ] Evaluate Neo4j adoption trigger criteria (Risk #4) based on real large-org graph query performance data.
- [ ] Evaluate Azure OpenAI Service as a configurable alternative to public OpenAI API for enterprise data-handling requirements (Risk #9).
- [ ] Stand up full OTel Collector + per-tenant tagged observability stack (Section 9.2, Phase 4 row).
- [ ] Add per-repository API key scoping if fast-follow decision (Risk #6) determines it's needed at launch rather than later.
