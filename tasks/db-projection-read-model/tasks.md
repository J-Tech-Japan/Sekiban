# DCB Materialized View — Task Breakdown

All tasks are for implementation work that follows merging this design PR. Nothing below is implemented yet.

---

## Package structure to be created

Two new projects will be added to `Sekiban.slnx` under the `Sekiban.Dcb` family:

1. **`Sekiban.Dcb.MaterializedView`** — core interfaces and abstractions (no database-specific code)
2. **`Sekiban.Dcb.MaterializedView.Postgres`** — PostgreSQL implementation

Neither package depends on `Sekiban.Dcb.Postgres` (the existing snapshot-based store). They are parallel.

---

## Phase 1 — MV Registry Core

**Package**: `Sekiban.Dcb.MaterializedView`

- [ ] **1.1** Define `MvRegistryEntry` record (service_id, view_name, view_version, logical_table, physical_table, status, current_position, target_position, last_sortable_unique_id, last_updated, metadata)
- [ ] **1.2** Define `MvActiveEntry` record (service_id, view_name, active_version, activated_at)
- [ ] **1.3** Define `MvStatus` enum (`Initializing`, `CatchingUp`, `Ready`, `Active`, `Retired`)
- [ ] **1.4** Define `IMvRegistryStore` interface with operations (`UpdatePositionAsync` / `UpdateStatusAsync` apply to all `logical_table` rows for a given `(viewName, viewVersion)` in one transactionally-consistent update):
  - `RegisterAsync(MvRegistryEntry entry)`
  - `UpdatePositionAsync(viewName, viewVersion, sortableUniqueId, IDbTransaction? tx)`
  - `UpdateStatusAsync(viewName, viewVersion, MvStatus status)`
  - `GetEntriesAsync(viewName, viewVersion)`
  - `GetActiveAsync(viewName)`
  - `SetActiveAsync(viewName, version)`
- [ ] **1.5** Define `PhysicalNameResolver` delegate `(viewName, version, logicalTable) -> string` with default implementation

## Phase 2 — Abstractions (`MvTable`, `MvSqlStatement`, Contexts)

**Package**: `Sekiban.Dcb.MaterializedView`

- [ ] **2.1** Define `MvTable` sealed class (LogicalName, PhysicalName, ViewName, ViewVersion)
- [ ] **2.2** Define `MvSqlStatement` record struct (Sql, Parameters)
- [ ] **2.3** Define `IMvInitContext` interface (`MvDbType`, Connection, RegisterTable, ExecuteAsync)
- [ ] **2.4** Define `IMvApplyContext` interface (`MvDbType`, Connection, Transaction, CurrentEvent, CurrentSortableUniqueId, read operations, IMvRow operations, GetDependencyViewTable)
- [ ] **2.5** Define `IMaterializedViewProjector` interface (ViewName, ViewVersion, InitializeAsync, ApplyToViewAsync)

## Phase 3 — `IMvRow` Row Abstraction

**Package**: `Sekiban.Dcb.MaterializedView`

- [ ] **3.1** Define `IMvRow` interface (ColumnCount, ColumnNames, IsNull, typed accessors for Guid/string/int/long/decimal/double/bool/DateTimeOffset/byte[], null-allowing variants, GetAs<T>, ToJson)
- [ ] **3.2** Define `IMvRowSet` interface (inherits `IReadOnlyList<IMvRow>` + ColumnNames)
- [ ] **3.3** Define `MvColumnAttribute` for property-to-column mapping overrides
- [ ] **3.4** Implement `MvRowMapper<T>` with Expression-tree compilation and caching, supporting both property-based models and record primary constructors / `init`-only members
- [ ] **3.5** Extension methods on `IMvApplyContext` (`QuerySingleOrDefaultAsync<T>`, `QueryAsync<T>`)

## Phase 4 — Row Metadata Conventions

**Package**: `Sekiban.Dcb.MaterializedView`

- [ ] **4.1** Define constants for standard metadata column names (`_last_sortable_unique_id`, `_last_applied_at`)
- [ ] **4.2** Document the idempotency pattern in XML doc comments on `IMvApplyContext.CurrentSortableUniqueId`
- [ ] **4.3** Provide a `MvSchemaHelper` static class with utility methods to generate metadata column SQL snippets (pure string helpers, no SQL execution)
- [ ] **4.4** Document that row metadata columns are recommended but not enforced

## Phase 5 — PostgreSQL Implementation

**Package**: `Sekiban.Dcb.MaterializedView.Postgres`

- [ ] **5.1** Implement `PostgresMvRegistryStore : IMvRegistryStore`
  - DDL for `sekiban_mv_registry` and `sekiban_mv_active` tables
  - All registry operations via Dapper (internal implementation detail)
- [ ] **5.2** Implement `PostgresMvInitContext : IMvInitContext`
- [ ] **5.3** Implement `PostgresMvApplyContext : IMvApplyContext`
- [ ] **5.4** Implement `PostgresMvRow : IMvRow` adapter over Dapper's `IDataReader` output
- [ ] **5.5** Implement `PostgresMvRowSet : IMvRowSet`
- [ ] **5.6** Wire Dapper as an internal dependency (not exposed through public API)
- [ ] **5.7** DI extension methods: `AddSekibanDcbMaterializedView(Action<MvOptions>)` and `AddSekibanDcbMaterializedViewPostgres(connectionString)`
- [ ] **5.8** Expose `MvDbType.Postgres` value

## Phase 6 — Catch-up Worker

**Package**: `Sekiban.Dcb.MaterializedView`

- [ ] **6.1** Define `IMvExecutor` interface (runs one or more MV projectors in catch-up mode)
- [ ] **6.2** Implement `MvCatchUpWorker` (IHostedService-based default implementation)
  - Reads events from `IEventStore.ReadAllSerializableEventsAsync`
  - Respects safe window (5s default)
  - Opens a transaction before `ApplyToViewAsync`, runs apply-time reads and returned statements in the same transaction/snapshot
  - Updates `current_position` atomically with writes
  - Handles restart by resuming from `registry.current_position`
- [ ] **6.3** Configuration: `MvWorkerOptions` (BatchSize, PollInterval, SafeWindowMs, etc.)
- [ ] **6.4** Multi-view support: one worker can manage multiple `IMaterializedViewProjector` instances

## Phase 7 — Version Management & Activation

**Package**: `Sekiban.Dcb.MaterializedView`

- [ ] **7.1** `IMvVersionManager` interface: `InitializeAsync`, `ActivateAsync`, `RetireAsync`, `DeleteAsync`
- [ ] **7.2** Implementation with state transition guards (only `Ready` → `Active`, etc.)
- [ ] **7.3** CLI-friendly methods for manual activation
- [ ] **7.4** Logging/telemetry on state transitions

## Phase 8 — Cross-view Reads

**Package**: `Sekiban.Dcb.MaterializedView`

- [ ] **8.1** Implement `GetDependencyViewTable(viewName, logicalTable)` in `IMvApplyContext`
- [ ] **8.2** Implement the typed variant `GetDependencyViewTable<TView>(logicalTable)`
- [ ] **8.3** Resolve tables from a persisted dependency version map, not from live `sekiban_mv_active` lookups
- [ ] **8.4** Cache dependency table lookups within a single apply operation
- [ ] **8.5** Document determinism and consistency caveats in XML docs
- [ ] **8.6** Unit test: replay after dependency activation change still resolves the originally pinned dependency version

## Phase 9 — Sample Application

**Location**: `internalUsages/` or `Samples/`

- [ ] **9.1** Create a sample `OrderSummaryMvV1 : IMaterializedViewProjector` demonstrating:
  - `RegisterTable("orders")`, `RegisterTable("items")`
  - CREATE TABLE with row metadata columns
  - `UpsertOrder`, `InsertItem`, `UpdateOrderTotal` helpers
  - Apply logic for `OrderCreated`, `OrderItemAdded`, `OrderCancelled`
- [ ] **9.2** Sample POCO types: `OrderRow`, `OrderItemRow`
- [ ] **9.3** Wire the sample into `DcbOrleans.AppHost` (as a separate optional service)
- [ ] **9.4** Manual verification doc: spin up the host, run commands, observe rows in Postgres

## Phase 10 — Testing

- [ ] **10.1** Unit tests for `ApplyToViewAsync` returning expected `MvSqlStatement` lists (no DB)
- [ ] **10.2** Integration tests with Testcontainers + PostgreSQL (real DB)
- [ ] **10.3** Idempotency test: replay same events twice, assert final state
- [ ] **10.4** Version coexistence test: v1 `active`, v2 `catching_up`, reads go to v1
- [ ] **10.5** Activation test: v2 reaches `ready`, activate, reads go to v2 atomically
- [ ] **10.6** Cross-view read test: `OrderSummaryMv` reads from `CustomerSummaryMv` through a pinned dependency version during apply
- [ ] **10.7** Safe window test: events within unsafe window are not yet applied
- [ ] **10.8** Recovery test: worker crashes mid-batch, on restart resumes from last committed position

## Phase 11 — Read-side API (Deferred)

Explicitly out of PoC scope. Planned follow-up:

- [ ] **11.1** `IMvQueryContext` with `For(viewName).Table(logicalTable).QueryAsync<T>(...)`
- [ ] **11.2** Raw SQL support with `{logical_table}` placeholders
- [ ] **11.3** Automatic `CREATE OR REPLACE VIEW sekiban_mv_view_*` generation for BI tool access
- [ ] **11.4** Query-side unit and integration tests

## Phase 12 — Explicit Dependencies & Topological Catch-up (Deferred)

- [ ] **12.1** `[DependsOnMv(typeof(CustomerSummaryMv))]` attribute or DI-based declaration
- [ ] **12.2** Topological sort of MVs during catch-up worker startup
- [ ] **12.3** Cycle detection (fail at startup with clear error)
- [ ] **12.4** Capture dependency version map during activation (or equivalent explicit checkpoint) and persist it for replay determinism
- [ ] **12.5** Wait-for-dependency policy during cross-view reads (configurable)

## Phase 13 — Runtime Protocol for Wasm/Remote Execution (Deferred)

- [ ] **13.1** Define serialized apply request/response contracts separate from `IMvApplyContext`
- [ ] **13.2** Define serialized read request/response contracts for row reads
- [ ] **13.3** Define serialized statement-list result contract separate from `MvSqlStatement`
- [ ] **13.4** Implement host adapters between the runtime protocol and the .NET authoring API
- [ ] **13.5** Add protocol compatibility tests against at least one non-.NET guest

---

## Out of scope entirely (not promised by this design)

- Entity Framework integration
- Cross-database projections (e.g., MV in Postgres reading from Cosmos)
- Automatic schema diffing between versions
- SQL dialect abstraction layers
- Query language DSL
- Multi-tenant isolation beyond name-prefix
