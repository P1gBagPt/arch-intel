# Architecture Intelligence Platform

## Vision

Architecture Intelligence Platform is an AI-first platform that continuously understands a software system at the architectural level rather than treating it as a collection of files.

Instead of requiring an LLM to rediscover a repository every time a question is asked, the platform builds and maintains a persistent architecture graph representing projects, dependencies, services, interfaces, entities, APIs, message flows, and relationships.

The graph becomes the single source of truth for:

* AI coding assistants
* Developers
* Technical leads
* Architects
* Engineering managers

The platform is designed around the principle that **AI should reason using architecture, not raw source code.**

---

# Goals

## Primary Goals

* Build a persistent understanding of a codebase.
* Generate implementation plans with architectural awareness.
* Perform dependency and impact analysis.
* Visualize large software systems.
* Expose architecture knowledge to AI agents through MCP.
* Keep the architecture continuously updated through incremental scanning.

## Secondary Goals

* Detect architectural issues.
* Measure coupling and complexity.
* Assist onboarding of new developers.
* Track architecture evolution over time.
* Eventually support multiple programming languages.

---

# High-Level Architecture

```text
                 Git Repository
                        │
                 Architecture Scanner
                        │
          Incremental Dependency Builder
                        │
               Architecture Graph Store
                        │
        ┌───────────────┼────────────────┐
        │               │                │
      MCP Server     REST API      WebSocket API
        │               │                │
 Claude/Codex      Next.js UI      Live Updates
```

---

# Core Components

## 1. Architecture Scanner

The scanner is responsible for building the architectural model.

Responsibilities:

* Scan solution structure
* Parse projects
* Parse source code
* Resolve references
* Build dependency graph
* Generate metadata
* Detect relationships

For .NET this will use Roslyn Semantic Model rather than simple syntax parsing.

Information extracted includes:

* Projects
* Assemblies
* Namespaces
* Classes
* Interfaces
* Records
* Enums
* Methods
* Constructors
* Dependency Injection registrations
* Controllers
* Minimal APIs
* MediatR handlers
* Domain Events
* Entity Framework entities
* Repositories
* Services
* Background workers
* Hosted services
* Message queues
* Configuration
* Tests

---

## 2. Graph Store

Instead of embeddings being the primary storage, architecture is represented as a graph.

Example:

```text
OrderController
        │
        ▼
IOrderService
        │
        ▼
OrderService
        │
        ▼
OrderRepository
        │
        ▼
SQL Server
```

Relationships include:

* References
* Calls
* Implements
* Inherits
* Injects
* Uses
* Publishes
* Consumes
* Owns
* Contains

---

## 3. Incremental Watcher

Instead of rebuilding the repository every scan:

```bash
arch watch
```

The watcher:

* detects changed files
* rebuilds affected nodes
* recalculates dependencies
* updates graph
* notifies connected clients

---

## 4. MCP Server

The MCP Server exposes architecture capabilities to AI agents.

Examples:

* Claude Code
* Codex CLI
* Cursor
* VS Code
* Future AI IDEs

The AI no longer searches repositories.

Instead it requests structured information.

Example:

```text
implementation_plan()

impact_analysis()

find_dependencies()

find_callers()

find_service()

generate_diagram()
```

---

## 5. REST API

The REST API powers the dashboard.

Example endpoints:

GET

* /projects
* /services
* /graph
* /impact
* /metrics

POST

* /implementation-plan
* /diagram
* /architecture-analysis

---

## 6. Next.js Dashboard

The web application visualizes architecture.

Views include:

### Repository Explorer

```
Projects

Business

Infrastructure

API

Tests
```

---

### Dependency Graph

Interactive graph showing:

* projects
* services
* interfaces
* entities

Users can:

* zoom
* pan
* filter
* search
* expand nodes

---

### Service Explorer

Selecting a service displays:

* dependencies
* callers
* implementations
* tests
* interfaces

---

### Impact Analysis

Selecting a class highlights every affected component.

Example:

```
ModelVersion

Affected

✓ API

✓ Repository

✓ Validators

✓ Tests

✓ Background Workers
```

---

### Architecture Timeline

Track architecture changes over time.

Example:

```
Today

2,350 classes

Yesterday

2,322 classes

Changes

+28 classes

+3 projects

-1 interface
```

---

### Coupling Heatmap

Projects are colored according to coupling.

Green

Stable

Yellow

Moderate

Red

Highly coupled

---

### AI Planner

Developers can type:

```
Implement Archive Model
```

The planner returns:

Affected projects

New files

Modified services

Database changes

Tests required

Risk level

Estimated effort

---

# CLI

Example commands

```bash
arch init

arch scan

arch watch

arch graph

arch explain OrderService

arch impact ModelVersion

arch callers IRepository

arch diagram Business

arch metrics

arch doctor
```

---

# Configuration

Example

```yaml
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

---

# Technology Stack

## Scanner

* C#
* .NET
* Roslyn
* MSBuild Workspace

Reason:

Roslyn provides semantic analysis and accurate symbol resolution.

---

## Backend

* ASP.NET Core Minimal APIs
* Background Services
* SignalR (live updates)

---

## Storage

Initial Version

* SQLite

Future

* PostgreSQL
* Neo4j (optional for advanced graph traversal)

---

## Search

* PostgreSQL + pgvector
* OpenAI Embeddings

Embeddings are used for documentation and semantic search, not architectural reasoning.

---

## AI Integration

* MCP Server
* OpenAI Responses API
* Claude Code
* Codex CLI
* Cursor

---

## Frontend

* Next.js
* React
* TypeScript
* Tailwind CSS
* React Flow (hierarchical views)
* Cytoscape.js or Sigma.js (large graph visualization)
* TanStack Query

---

## Authentication (Future)

* Better Auth
* GitHub OAuth
* Microsoft Entra ID

---

## Deployment

Dashboard

* Vercel or Azure Static Web Apps

API

* Azure App Service
* Docker
* Railway
* Fly.io

Local Scanner

Distributed as:

* npm package
* .NET global tool

---

# Roadmap

## Phase 1

* Solution scanner
* Dependency graph
* SQLite storage
* CLI
* Basic MCP server

---

## Phase 2

* Next.js dashboard
* Interactive dependency graph
* Impact analysis
* Mermaid export
* Architecture explorer

---

## Phase 3

* AI implementation planner
* Incremental watcher
* Architecture metrics
* Coupling analysis
* Circular dependency detection

---

## Phase 4

* Cloud synchronization
* Team collaboration
* Historical architecture snapshots
* Multi-repository support
* Architecture quality scoring

---

# Long-Term Vision

The platform aims to become the architectural intelligence layer for software engineering.

Rather than replacing existing AI coding assistants, it augments them by providing persistent, structured architectural knowledge. Every AI assistant connected through MCP can reason about the system with an understanding of dependencies, design boundaries, and implementation impact.

The long-term objective is to evolve from a local developer tool into a platform that enables organizations to visualize, analyze, and collaborate on the living architecture of their software systems.
