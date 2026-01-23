# Sekiban Repository Overview (Agent Notes)

## Goal of This Document
This file provides a high‑level map of the repository so an agent can quickly understand where to work. The main active development focus is **DCB** under `dcb/`, while **aggregate‑based event sourcing** lives under `pure/`, and **TypeScript + Dapr** lives under `ts/`.

## Top‑Level Structure
- `dcb/` — **Primary development area** for DCB (Dynamic Consistency Boundary) implementation in C#.
- `pure/` — **Aggregate‑based Event Sourcing** implementation in C# (Sekiban.Pure).
- `ts/` — **TypeScript + Dapr** implementation (alpha) and related packages/tools.
- `docs/` — Documentation sources.
- `templates/` — Project templates (C# and Dapr variants).
- `Samples/` — Example applications.
- `tools/` — Supporting utilities/scripts.
- `tasks/` — Design or task tracking materials.

## DCB (Primary Focus)
- Path: `dcb/`
- Purpose: Implement the DCB runtime and its event store integrations.
- Typical work includes:
  - `IEventStore` implementations (e.g., PostgreSQL, Cosmos DB)
  - Actor/Grain logic for consistency boundaries
  - Query/projection pipelines
- If you’re unsure where to start for DCB changes, inspect:
  - `dcb/src/Sekiban.Dcb.Core/`
  - `dcb/src/Sekiban.Dcb.*` provider projects
  - `dcb/tests/` for behavior expectations

## Pure (Aggregate‑Based ES, C#)
- Path: `pure/`
- Purpose: Aggregate‑style event sourcing (Sekiban.Pure) with Orleans/Dapr integrations.
- Use this when the task is explicitly about aggregate‑based flows, non‑DCB runtime, or Pure templates.
- Key references:
  - `README_Sekiban_Pure.md`
  - `README_Sekiban_Pure_JP.md`
  - `pure/src/` and `pure/internalUsages/`

## TypeScript + Dapr (Alpha)
- Path: `ts/`
- Purpose: TypeScript implementation of event sourcing with Dapr actors.
- Use this when tasks reference Node.js/TypeScript packages, Dapr actor patterns, or TS project templates.
- Entry point: `ts/` README and package directories inside.

## Where to Look First
- **Repository intent**: `README.md`
- **DCB conceptual docs**: `tasks/dcb.design/` (design notes and interfaces)
- **Pure docs**: `README_Sekiban_Pure.md`
- **TS docs**: `ts/` README(s)

## General Working Notes
- This repo contains **multiple implementations** (DCB, Pure, TS). Confirm scope before editing.
- Prefer changes under `dcb/` unless a task explicitly targets `pure/` or `ts/`.
- When adding new providers or storage backends, check existing patterns in `dcb/src/Sekiban.Dcb.*`.

## DCB Storage Provider Patterns

When implementing a new storage provider (like DynamoDB, Cosmos DB, Postgres), follow these patterns:

### Required Interfaces
- `IEventStore` — Event read/write operations
- `IMultiProjectionStateStore` — Projection state persistence

### Package Structure
```
dcb/src/Sekiban.Dcb.{Provider}/
├── {Provider}EventStore.cs           # IEventStore implementation
├── {Provider}Context.cs              # Connection/table management
├── {Provider}Initializer.cs          # HostedService for setup
├── {Provider}EventStoreOptions.cs    # Configuration options
├── {Provider}MultiProjectionStateStore.cs
├── SekibanDcb{Provider}Extensions.cs # DI registration
└── Models/                           # Storage-specific models
```

### Blob Storage Offloading
For storage backends with item size limits (DynamoDB: 400KB, Cosmos: 2MB), implement:
- A separate `Sekiban.Dcb.BlobStorage.{CloudProvider}` package
- Implements `IBlobStorageSnapshotAccessor` interface from Core
- Examples: `Sekiban.Dcb.BlobStorage.AzureStorage`, `Sekiban.Dcb.BlobStorage.S3`

### Reference Implementations
- **Cosmos DB**: `dcb/src/Sekiban.Dcb.CosmosDb/` — Full implementation with Azure Blob offloading
- **Postgres**: `dcb/src/Sekiban.Dcb.Postgres/` — EF Core based implementation
- **SQLite**: `dcb/src/Sekiban.Dcb.Sqlite/` — Simple local implementation

## Task Tracking
- Design documents and task materials are stored in `tasks/` directory
- Issue-specific designs: `tasks/{issue-number}/design-{agent}/`

