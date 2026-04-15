# DCB Materialized View (MV)

This folder contains the design and task breakdown for adding **DCB Materialized View** — a new, parallel projection mechanism that writes typed rows to real SQL tables — to Sekiban DCB.

Today, `IMultiProjectionStateStore` implementations (Postgres/Cosmos/DynamoDB/SQLite) persist a single serialized snapshot per projector. This proposal adds a **separate, parallel subsystem** that materializes projection output to actual tables so that read models can be queried with normal SQL / BI tooling.

**No implementation is included in this PR** — only design and task documents.

## Why a new name ("Materialized View" / `Mv*`)

This feature is intentionally named to avoid collision with existing DCB concepts:

| Existing in DCB | New (this proposal) |
|---|---|
| `ICoreMultiProjector<T>` | `IMaterializedViewProjector` |
| `MultiProjectionStateBuilder` | `MvCatchUpWorker` |
| `dcb_multi_projection_states` | `sekiban_mv_registry` + `sekiban_mv_active` |
| `IMultiProjectionStateStore` | `IMvRegistryStore` + `IMvExecutor` |

Every publicly exposed new type uses the full word `MaterializedView` or the short prefix `Mv*`, so code search and IntelliSense stay unambiguous. Shared primitives (`IEvent`, `SortableUniqueId`, `IEventStore`, `ServiceId`) are reused intentionally.

> **Note**: "Materialized view" here is used in the general database sense ("a query result stored as a real table"). It does NOT mean PostgreSQL's built-in `MATERIALIZED VIEW` SQL object — we use normal CREATE TABLE statements written by the developer.

## Contents

| File | Purpose |
|---|---|
| `design.md` | Full design document (lifecycle, object model, context APIs, cross-view reads, WASM marshaling, ORM layers, name-collision audit) |
| `tasks.md` | Task breakdown into phases (Phase 1 → Phase 11) and the scope of the first PoC sprint |
| `poc-scope.md` | Minimum viable PoC and acceptance criteria |
| `open-questions.md` | Open questions captured during design, grouped by category |
| `integration-notes.md` | Notes on how this fits into existing DCB architecture (relationship to `ICoreMultiProjector`, `IMultiProjectionStateStore`, `GeneralMultiProjectionActor`, safe/unsafe state) |

## Motivation (one paragraph)

DCB projections currently materialize a single in-memory state that is snapshotted to storage as a gzipped JSON blob. This works well for small/medium projections but has limitations:

- Cannot be queried directly with SQL / BI tools
- Memory pressure scales with projection size
- No schema evolution story for projection internals
- Not ideal as a read model for application queries that need `WHERE` / `JOIN` / index access

The DCB Materialized View subsystem provides a parallel option: projection logic still consumes events, but writes are emitted as a list of `MvSqlStatement`s that the framework executes against real tables in one transaction per event. The table shape, indexes, and queries become normal database artifacts.

## Relationship to existing `ICoreMultiProjector<T>`

This proposal does **not** replace the existing multi-projector model. Both coexist:

- `ICoreMultiProjector<T>` — in-memory state, snapshotted as blob (existing, unchanged)
- `IMaterializedViewProjector` (new) — row-level state, writes to real tables

The same `IEventStore`, `SortableUniqueId`, safe/unsafe window semantics, and shared DCB primitives are used by both.

## Core design principles

1. **Two-phase lifecycle**: `InitializeAsync` (once) + `ApplyToViewAsync` (per event)
2. **Developer writes SQL**: No SQL dialect abstraction in the framework
3. **Writes are returned, not executed**: `ApplyToViewAsync` returns `IReadOnlyList<MvSqlStatement>`, framework executes them in one transaction
4. **Row metadata for idempotency**: `_last_sortable_unique_id` column enables safe replay
5. **Cross-view reads**: Read from another MV's active-version tables via a context helper
6. **ORM via layered `IMvRow`**: Framework core exposes `IMvRow`/`IMvRowSet`, mappers sit on top, Dapper is an internal implementation detail
7. **WASM-friendly**: All boundary data is JSON/MessagePack-friendly, SQL strings pass freely
8. **Name-clash-free**: Every new public type uses `MaterializedView` or `Mv*` prefix

## This PR contains ONLY

- Design documents (`design.md`, `tasks.md`, `poc-scope.md`, `open-questions.md`, `integration-notes.md`)

## This PR does NOT contain

- Any `.cs` files
- Any changes to existing DCB source code
- Any project file changes (`.csproj`, `.slnx`)
- Any test projects

Implementation is a follow-up effort, tracked via the task breakdown in `tasks.md`.
