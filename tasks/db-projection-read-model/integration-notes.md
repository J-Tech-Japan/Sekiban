# DCB Materialized View — Integration Notes

How the new DCB Materialized View subsystem fits alongside the existing DCB code. This document is the reference for reviewers who want to understand what will change (and what will NOT change) when the MV implementation lands.

---

## 1. Existing DCB concepts the MV subsystem reuses (unchanged)

| Concept | Source location | Usage by MV |
|---|---|---|
| `IEvent` | `Sekiban.Dcb.Core.Model/Events/` | Input to `ApplyToViewAsync` |
| `SortableUniqueId` | `Sekiban.Dcb.Core.Model/Common/SortableUniqueId.cs` | Position tracking in registry, idempotency guard |
| `IEventStore` | `Sekiban.Dcb.Core/Storage/IEventStore.cs` | Reads events for catch-up via `ReadAllSerializableEventsAsync` |
| `ServiceId` / `IServiceIdProvider` | existing | Multi-tenant isolation in `sekiban_mv_registry` and `sekiban_mv_active` |
| `ResultBox<T>` | `ResultBoxes` package | Used for public method return types consistent with existing DCB style |
| `SortableUniqueId.SafeMilliseconds` (5000ms) | existing | MV catch-up worker respects the same safe window |

**Nothing in the above list changes**. MV implementation only reads these types and calls their methods.

---

## 2. Existing DCB concepts the MV subsystem deliberately does NOT reuse

| Concept | Why not |
|---|---|
| `ICoreMultiProjector<T>` | MV has a completely different authoring model (instance interface vs static abstract, row writes vs record transforms, DB state vs in-memory state). Sharing the interface would force confusing compromises. |
| `MultiProjectionStateBuilder` | MV has its own `MvCatchUpWorker`. The existing builder is deeply tied to blob snapshot semantics. |
| `IMultiProjectionStateStore` | MV has its own `IMvRegistryStore`. Different storage shape, different contract. |
| `DbMultiProjectionState` (Postgres) | MV has its own tables `sekiban_mv_registry` and `sekiban_mv_active`. |
| `SafeUnsafeProjectionState<K,V>` | MV does not use in-memory dual-state; the safe window is applied at the worker level (see §5.4). |
| `SafeProjection<T>` / `UnsafeProjection<T>` | Same reason. |
| `GeneralMultiProjectionActor` / related Orleans grains | MV runs in `IHostedService` (PoC); Orleans integration is deferred. |
| `MultiProjectionStateRecord` / `MultiProjectionStateWriteRequest` | MV has its own record types (`MvRegistryEntry`, `MvActiveEntry`). |

This is a **deliberate parallel subsystem**, not an extension of the existing one.

---

## 3. What is added to the repository

### 3.1 New projects

```
dcb/src/Sekiban.Dcb.MaterializedView/
├── Sekiban.Dcb.MaterializedView.csproj
├── Abstractions/
│   ├── IMaterializedViewProjector.cs
│   ├── MvTable.cs
│   ├── MvSqlStatement.cs
│   ├── IMvInitContext.cs
│   ├── IMvApplyContext.cs
│   ├── IMvRow.cs
│   ├── IMvRowSet.cs
│   └── MvColumnAttribute.cs
├── Registry/
│   ├── IMvRegistryStore.cs
│   ├── MvRegistryEntry.cs
│   ├── MvActiveEntry.cs
│   ├── MvStatus.cs
│   └── PhysicalNameResolver.cs
├── Mapping/
│   └── MvRowMapper.cs
├── Execution/
│   ├── IMvExecutor.cs
│   ├── MvCatchUpWorker.cs
│   └── MvWorkerOptions.cs
└── Helpers/
    └── MvSchemaHelper.cs

dcb/src/Sekiban.Dcb.MaterializedView.Postgres/
├── Sekiban.Dcb.MaterializedView.Postgres.csproj
├── PostgresMvRegistryStore.cs
├── PostgresMvInitContext.cs
├── PostgresMvApplyContext.cs
├── PostgresMvRow.cs
├── PostgresMvRowSet.cs
└── ServiceCollectionExtensions.cs
```

### 3.2 Changes to existing files

**The implementation PR will touch exactly these existing files** (listed here for review planning):

- `Sekiban.slnx` — add two new projects
- `dcb/src/Directory.Build.props` — include the new projects in the family if a family-level setting exists
- `dcb/src/Directory.Packages.props` — add `MessagePack` if MessagePack is adopted (not for PoC); `Dapper` likely already exists
- `internalUsages/DcbOrleans.AppHost/` — register a sample MV and its worker (PoC acceptance criterion #1)

**Everything else in `dcb/src/Sekiban.Dcb.Core`, `Sekiban.Dcb.Core.Model`, `Sekiban.Dcb.Postgres`, `Sekiban.Dcb.CosmosDb`, `Sekiban.Dcb.Sqlite`, `Sekiban.Dcb.DynamoDB`, `Sekiban.Dcb.Orleans.Core`, etc. — is untouched.**

### 3.3 Changes in THIS design PR

Only the following files are added in this design PR:

```
tasks/db-projection-read-model/
├── README.md
├── design.md
├── tasks.md
├── poc-scope.md
├── open-questions.md
└── integration-notes.md
```

**No code. No csproj. No sln changes.**

---

## 4. Comparison table: existing multi-projector vs new MV

| Aspect | `ICoreMultiProjector<T>` (existing) | `IMaterializedViewProjector` (new) |
|---|---|---|
| Interface style | `static abstract` members | instance interface |
| State shape | In-memory record tree `T` | Rows in real SQL tables |
| Persistence | Gzipped JSON blob in `dcb_multi_projection_states` | Real tables in `sekiban_mv_*_v*_*` |
| Snapshot build | `MultiProjectionStateBuilder` rebuilds from events | `MvCatchUpWorker` applies events incrementally |
| Storage backend | Pluggable (`IMultiProjectionStateStore`): Postgres/Cosmos/DynamoDB/SQLite | Pluggable (`IMvRegistryStore` + Dapper): Postgres (first), others later |
| Version identifier | `MultiProjectorVersion` (string) | `ViewVersion` (int) |
| Write semantics | Return new `T` from `Project` | Return `IReadOnlyList<MvSqlStatement>` from `ApplyToViewAsync` |
| Transaction boundary | Entire snapshot write | Per-event (returned list = 1 transaction) |
| Reads during apply | All state in memory | SQL reads via context (`QuerySingleOrDefaultAsync` etc.) |
| Cross-projector reads | Not directly supported | Supported via `GetActiveViewTable` |
| Safe/unsafe handling | `SafeUnsafeProjectionState<K,V>` in memory | Worker delays apply of unsafe events |
| Actor hosting | Orleans `GeneralMultiProjectionActor` | `IHostedService` (PoC), Orleans grain later |
| BI tool queryable | No (blob) | Yes (real tables) |
| Size scalability | Limited by blob size + offload | Limited by PostgreSQL |
| Schema evolution | Replace `MultiProjectorVersion`, rebuild from scratch | Create new version alongside, catch up, atomic switch |

The two models solve overlapping but distinct problems. Users will pick based on their read-access patterns and data size.

---

## 5. Semantic parity guarantees (what MV does NOT promise)

- MV does NOT guarantee it produces identical state to an equivalent `ICoreMultiProjector<T>`. They have different write semantics.
- MV does NOT participate in DCB's conditional-append / optimistic concurrency mechanism. MV is pure read-model materialization, downstream of the event store.
- MV does NOT try to be an actor model. Catch-up is a background job; query access is direct DB access.
- MV does NOT expose its state via Orleans grains (in PoC). If Orleans-based query access is needed later, that's a follow-up effort.

---

## 6. Data ownership and coexistence

One DCB service can run both:

```
┌──────────────────────────────────────────────┐
│ Event Store (dcb_events, dcb_tags)           │  ← shared source of truth
└──────────┬───────────────────────────────────┘
           │
     ┌─────┴─────┐
     ▼           ▼
┌─────────┐  ┌───────────────┐
│ Multi   │  │ Materialized  │
│ Projector│ │ View          │
│ (blob)  │  │ (tables)      │
└─────────┘  └───────────────┘
     │              │
     ▼              ▼
dcb_multi_   sekiban_mv_registry
projection_  sekiban_mv_active
states       sekiban_mv_ordersummary_v1_*
             sekiban_mv_customersummary_v1_*
```

Both consume the same event store; neither writes to the other's tables. They can be mixed freely in the same application.

---

## 7. Migration path (existing → MV)

For users who want to migrate an existing `ICoreMultiProjector<T>` to a materialized view:

1. Keep the existing projector running (zero risk)
2. Author a new `IMaterializedViewProjector` with equivalent logic
3. Run both in parallel; compare query results over time
4. When confident, switch application reads to the MV
5. Eventually retire the old projector

There is no automated migration tool. The design PR's AI-conversion mapping (see `design.md` §10 in the `ICoreMultiProjector` version of the source, or the project README) serves as guidance for machine-assisted translation.

---

## 8. Review checklist for the implementation PR (future)

When the PoC implementation PR comes in, reviewers should verify:

- [ ] No changes to any file under `dcb/src/Sekiban.Dcb.Core/MultiProjections/`
- [ ] No changes to any file under `dcb/src/Sekiban.Dcb.Core.Model/MultiProjections/`
- [ ] No changes to any file under `dcb/src/Sekiban.Dcb.Postgres/` (except possibly shared Dapper version bumps via `Directory.Packages.props`)
- [ ] No changes to `GeneralMultiProjectionActor` or any `MultiProjectionStateBuilder`-related code
- [ ] No changes to `IMultiProjectionStateStore` implementations (Postgres/Cosmos/SQLite/DynamoDB)
- [ ] New projects `Sekiban.Dcb.MaterializedView` and `Sekiban.Dcb.MaterializedView.Postgres` have their own test projects under `dcb/tests/` or equivalent
- [ ] All public types use `MaterializedView` / `Mv*` naming — no accidental `Projection` / `Projector` reuse
- [ ] `sekiban_mv_*` table prefix is used consistently; no collisions with existing `dcb_*` tables
- [ ] `ServiceId` is honored everywhere (all registry operations are service-scoped)
