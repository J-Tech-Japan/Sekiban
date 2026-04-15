# DCB Materialized View — Design Document

**Status**: Proposal (design-only, no implementation in this PR)
**Scope**: New .NET authoring API in `Sekiban.Dcb.MaterializedView` plus `Sekiban.Dcb.MaterializedView.Postgres`, with a future language-neutral runtime protocol for Wasm/remote execution, integrated with the existing event store
**Working name**: **"DCB Materialized View"** (DCB MV / `Mv*`). The feature is distinct from the existing multi-projection model and uses a separate name to avoid confusion.

---

## 1. Background

### 1.1 Current multi-projection persistence

Sekiban DCB today has exactly one way to persist a multi-projection:

- `ICoreMultiProjector<T>` (in `Sekiban.Dcb.Core.Model/MultiProjections/IMultiProjector.cs`) defines a **static abstract** `Project(payload, ev, tags, domainTypes, safeWindowThreshold)` method.
- A projector produces an in-memory state `T` (typically a record tree or `SafeUnsafeProjectionState<TKey, TState>`).
- `MultiProjectionStateBuilder` rebuilds the state from the event store and hands it to `IMultiProjectionStateStore` implementations (`PostgresMultiProjectionStateStore`, `CosmosMultiProjectionStateStore`, etc.).
- The state is serialized to JSON, gzip-compressed, and stored as a blob in `dcb_multi_projection_states` (`DbMultiProjectionState.StateData`). Large payloads can be offloaded to blob storage.
- `SortableUniqueId` (30-char string: 19 ticks + 11 random) provides global ordering, and safe/unsafe state is managed via `SafeUnsafeProjectionState`.

This is excellent for correctness and replay semantics but cannot be queried directly via SQL.

### 1.2 What is missing

Nothing in the current code writes projection output to actual database tables with typed columns. The `Sekiban.Dcb.Postgres` package stores the projector snapshot blob — not the projected data itself. There is no row-level materialization, no table schema per projector, no `UPDATE / SELECT` based projector authoring.

### 1.3 What this proposal adds

**A brand-new, parallel mechanism** called a **DCB Materialized View** (written `MaterializedView`, shortened `Mv`). It coexists with `ICoreMultiProjector<T>`, sharing `IEventStore`, `SortableUniqueId`, and orchestration, but writes **row-level state to real database tables**.

Both can be used in the same application:
- Use `ICoreMultiProjector<T>` when state is small, accessed purely in-memory, or needs actor-hosted queries
- Use `IMaterializedViewProjector` when you want SQL-queryable read models, BI tool integration, or large datasets that don't fit well in a single blob

### 1.4 Naming rationale

The term **Materialized View** was chosen deliberately because:

1. It is the standard database community term for "a query result stored as a real table"
2. It does NOT collide with DCB's existing `MultiProjection`, `MultiProjector`, `MultiProjectionState*` types
3. "Materialized" immediately conveys that data lives in real tables, not in memory
4. The `Mv*` prefix gives a short, consistent, searchable abbreviation in code and SQL
5. In this document and in code:
   - `MaterializedView` refers to **the projection mechanism as a whole** (one logical read model, possibly with multiple tables)
   - `MvTable` refers to **a single physical table** inside a materialized view
   - `MvSqlStatement` is a parameter + SQL pair returned from an apply method
   - `Mv*Context` names the framework-supplied apply/init contexts

> **Important**: "Materialized view" in this document does NOT mean PostgreSQL's built-in `MATERIALIZED VIEW` SQL object. We use the term conceptually. The implementation uses normal CREATE TABLE statements written by the developer; PostgreSQL's `MATERIALIZED VIEW` feature is unrelated and not used here.

---

## 2. Goals and Non-goals

### Goals

1. Let developers define a materialized view whose output is **rows in typed SQL tables**
2. Support **multiple versions** of the same materialized view coexisting (v1 active, v2 catching up)
3. Support **1 materialized view → multiple tables** (e.g., `OrderSummaryMv` → `orders` + `items`)
4. Make the developer experience familiar to users of `ICoreMultiProjector<T>` — "same mental model, different target"
5. Keep **SQL authorship in the developer's hands** — no SQL dialect abstraction leaks
6. Provide **row metadata** for idempotency and debugging (`_last_sortable_unique_id`)
7. Allow **cross-view reads** during apply, via a context helper that resolves a dependency-pinned version of another materialized view rather than a live mutable active pointer
8. Keep the **.NET authoring API idiomatic** while reserving a separate language-neutral runtime protocol for future Wasm/remote execution
9. Keep all names distinct from existing DCB types so code search and IntelliSense stay unambiguous

### Non-goals

1. Replacing `ICoreMultiProjector<T>` — it continues to exist and is unchanged
2. Providing a cross-database ORM — each DB has its own SQL dialect and that's fine
3. Providing query abstractions on the read side in this iteration (read-side API is planned future work)
4. Migration tooling for schema evolution (covered at a high level, but detailed migration DSL is out of scope)
5. Multi-tenant isolation beyond simple name prefix override (out of scope for v1)

---

## 3. Core concepts

### 3.1 Two-phase lifecycle

A DCB materialized view has exactly two phases:

```
┌────────────────────────────┐   ┌─────────────────────────────────────┐
│ Phase 1: InitializeAsync   │──▶│ Phase 2: ApplyToViewAsync (per event)│
│ (once per view + version)  │   │  - read (immediate)                  │
│                            │   │  - build MvSqlStatement list          │
│  - RegisterTable           │   │  - return list to framework           │
│  - CREATE TABLE/INDEX      │   │  - framework runs in 1 transaction    │
└────────────────────────────┘   └─────────────────────────────────────┘
```

**Phase 1 (Initialize)** runs once when the view version is first registered in the MV registry. It creates the physical tables, indexes, and any preparatory structures.

**Phase 2 (Apply)** runs for every event during catch-up and steady-state. It consumes an event, optionally reads existing rows, and returns a list of `MvSqlStatement`s describing the writes. The framework opens a transaction first, binds the apply context to that transaction/snapshot, performs the reads through that context, then executes the returned list and advances `current_position` in the same transaction.

The apply method is named `ApplyToViewAsync` (not `ApplyAsync` or `ProjectAsync`) to make its purpose unmistakable and avoid visual confusion with `ICoreMultiProjector.Project` when both types appear in the same file.

### 3.2 Writes are returned, not executed

The key design choice: `ApplyToViewAsync` does **not** execute writes. It returns `IReadOnlyList<MvSqlStatement>`.

```csharp
public readonly record struct MvSqlStatement(string Sql, object? Parameters = null);
```

This has several benefits:

- **Atomicity is explicit** — the returned list is one transaction
- **Order is explicit** — list order = execution order
- **Testing is pure** — the returned list is the output, assertable without a database
- **Dry-run / logging is trivial** — you have all the SQL as data
- **Pipeline optimization** — framework can log/audit/transform before executing

Reads **are** executed immediately through the context, since apply logic often needs to read existing rows and branch on them. The important contract is that those reads happen against the **same transaction/snapshot** that later executes the returned writes, so the read-modify-write cycle stays consistent.

### 3.3 Developer writes SQL; framework abstracts nothing

The framework does **not** provide `Upsert(row)`, `Insert(row)`, `Update(row)` helpers that generate SQL. Instead the developer writes SQL strings directly. Rationale:

- SQL dialects diverge deeply (JSONB, UPSERT, interval types, partitioning). Any abstraction leaks.
- Dapper already gives a great minimal API surface; we don't need another layer
- Debugging and performance tuning are far easier when you see the actual SQL in your code
- When a view needs to target two DBs, the developer `switch`es on `ctx.DatabaseType` once — no framework-wide dialect system needed

The framework provides:
- **Physical table name resolution** (via `MvTable.PhysicalName`)
- **A Dapper-free `IMvRow` row abstraction** for read results
- **Context-level helpers** for executing reads and resolving cross-view tables
- **A row mapper** (`IMvRow → T`) with convention-based defaults

Everything else — the `CREATE TABLE`, the `INSERT ... ON CONFLICT DO UPDATE`, the `SELECT ... WHERE ... JOIN ...` — is written by the developer in plain SQL.

### 3.4 Row metadata for idempotency and tracking

Every MV table is expected (or recommended) to include two standard columns:

```sql
_last_sortable_unique_id TEXT NOT NULL,
_last_applied_at         TIMESTAMPTZ NOT NULL DEFAULT NOW()
```

This enables:

- **Idempotent replays**: `WHERE _last_sortable_unique_id < @sid` prevents older events from overwriting newer state
- **Catch-up progress tracking at the row level** (complements registry-level `current_position`)
- **Cross-view integrity checks**: a reader view can verify another view has reached a given `SortableUniqueId`

The `IMvApplyContext` exposes `CurrentSortableUniqueId` so user code can embed it trivially in writes.

### 3.5 Cross-view reads

Apply logic sometimes needs to read from **another** materialized view to compute its writes (e.g., `OrderSummaryMv.ApplyToViewAsync(OrderItemAdded)` reads a discount tier from `CustomerSummaryMv`). This must **not** resolve "whatever version is active right now", because replaying the same event stream after an activation change would otherwise produce different results.

When cross-view reads are added, they are supported through a **dependency-pinned** resolver:

```csharp
MvTable GetDependencyViewTable(string viewName, string logicalTable);
MvTable GetDependencyViewTable<TView>(string logicalTable)
    where TView : IMaterializedViewProjector;
```

The resolution uses a **pinned dependency version map** captured for the current view version and stored in MV metadata. The apply context resolves physical table names from that pinned map, not from a live `sekiban_mv_active` lookup on every call. Writes are forbidden across views — you can only read from another view. Your own view's writes are the only thing returned from `ApplyToViewAsync`.

**Consistency caveat**: during catch-up, the dependency view may still be at an earlier `current_position` than your view. This is an eventually-consistent system; the contract here is deterministic replay, not global serializability.

---

## 4. Object model

```
IMaterializedViewProjector                 (user implements)
 ├── MvTable Orders                        (holds logical & physical names)
 ├── MvTable Items                         (holds logical & physical names)
 ├── InitializeAsync(IMvInitContext)
 └── ApplyToViewAsync(IEvent, IMvApplyContext) → IReadOnlyList<MvSqlStatement>

MvSqlStatement (record struct)             (value type, framework-executed)

IMvInitContext                             (framework provides)
 ├── DatabaseType, Connection
 ├── RegisterTable(logicalName): MvTable
 └── ExecuteAsync(sql, params)              ← used for CREATE TABLE/INDEX

IMvApplyContext                            (framework provides)
 ├── DatabaseType, Connection, Transaction
 ├── CurrentEvent, CurrentSortableUniqueId
 ├── QuerySingleOrDefaultAsync<T>(sql, params)
 ├── QueryAsync<T>(sql, params)
 ├── QuerySingleOrDefaultRowAsync(sql, params): IMvRow?
 ├── QueryRowsAsync(sql, params): IMvRowSet
 └── GetDependencyViewTable(viewName, logicalTable): MvTable
```

### 4.1 `IMaterializedViewProjector`

```csharp
public interface IMaterializedViewProjector
{
    string ViewName { get; }
    int ViewVersion { get; }

    Task InitializeAsync(IMvInitContext ctx);

    Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(
        IEvent ev,
        IMvApplyContext ctx);
}
```

This has a purposely different shape from `ICoreMultiProjector<T>`:

| Aspect | `ICoreMultiProjector<T>` | `IMaterializedViewProjector` |
|---|---|---|
| Member style | `static abstract` | instance |
| State | In-memory `T` payload | No in-memory state; row data in DB |
| Write path | `return new T with {...}` | `return [new MvSqlStatement(...)]` |
| Key identifier | `MultiProjectorName` | `ViewName` |
| Version | `MultiProjectorVersion` (string) | `ViewVersion` (int) |
| Persistence | blob snapshot | real tables |

The name differences (`ViewName` vs `MultiProjectorName`, `ApplyToViewAsync` vs `Project`) are intentional — code search for either term returns only its own ecosystem.

### 4.2 `MvTable`

```csharp
public sealed class MvTable
{
    public string LogicalName { get; }
    public string PhysicalName { get; }    // resolved during Initialize
    public string ViewName { get; }        // owner view name
    public int    ViewVersion { get; }     // owner view version
}
```

A thin value holder. It does not carry SQL generation methods. The developer reads `PhysicalName` and embeds it in SQL strings. The `ViewName`/`ViewVersion` are kept for debugging and to support cross-view sanity checks.

### 4.3 `MvSqlStatement`

```csharp
public readonly record struct MvSqlStatement(string Sql, object? Parameters = null);
```

A value type. `Sql` is a raw SQL string (parameterized via `@name` placeholders). `Parameters` is an anonymous object or POCO compatible with Dapper's parameter model.

`MvSqlStatement` is part of the **.NET authoring surface**, not the future cross-language ABI. A Wasm/remote runtime will use a separate serialized contract and a host adapter that converts the protocol's parameter payload into this host-side form.

The name `MvSqlStatement` was chosen over plain `SqlStatement` to avoid ambiguity with any general-purpose "sql statement" type that might exist elsewhere in DCB or the broader .NET ecosystem.

### 4.4 Contexts

```csharp
public interface IMvInitContext
{
    MvDbType DatabaseType { get; }
    IDbConnection Connection { get; }

    MvTable RegisterTable(string logicalName);
    Task ExecuteAsync(string sql, object? param = null);
}

public interface IMvApplyContext
{
    MvDbType DatabaseType { get; }
    IDbConnection Connection { get; }
    IDbTransaction Transaction { get; }

    IEvent CurrentEvent { get; }
    string CurrentSortableUniqueId { get; }

    // Read operations (immediate)
    Task<T?>              QuerySingleOrDefaultAsync<T>(string sql, object? param = null);
    Task<T>               QuerySingleAsync<T>(string sql, object? param = null);
    Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? param = null);
    Task<TScalar>         ExecuteScalarAsync<TScalar>(string sql, object? param = null);

    // IMvRow-level read (for mapper-free use)
    Task<IMvRow?>   QuerySingleOrDefaultRowAsync(string sql, object? param = null);
    Task<IMvRowSet> QueryRowsAsync(string sql, object? param = null);

    // Cross-view table resolution
    MvTable GetDependencyViewTable(string viewName, string logicalTable);
    MvTable GetDependencyViewTable<TView>(string logicalTable)
        where TView : IMaterializedViewProjector;
}
```

`IMvInitContext` and `IMvApplyContext` are **.NET host-side authoring interfaces**. They are intentionally idiomatic .NET and are not themselves the Wasm/remote ABI. Future multi-language execution uses a separate serialized runtime protocol whose host adapter provides these interfaces to the .NET implementation.

`MvDbType` is a small MV-specific enum (`Postgres`, future `Sqlite`, etc.). It is intentionally named to avoid confusion with `System.Data.DbType`, whose values represent parameter/column types rather than a database engine.

`IDbConnection` is exposed as an escape hatch — developers who want to use Dapper directly (or Npgsql's COPY, or anything else) can do so without fighting the framework. `IMvApplyContext.Transaction` and all read methods are bound to the same transaction/snapshot that later executes the returned statements.

---

## 5. Physical layout

### 5.1 Physical table naming

```
{prefix}_{view_name}_{version}_{logical_table}
```

Defaults:
- `prefix` = `sekiban_mv`
- `view_name` = `IMaterializedViewProjector.ViewName` lowercased, `[A-Za-z0-9_]` only
- `version` = `v{ViewVersion}`
- `logical_table` = name passed to `RegisterTable`

Examples:

- `sekiban_mv_ordersummary_v1_orders`
- `sekiban_mv_ordersummary_v2_orders` (v2 coexists with v1)
- `sekiban_mv_ordersummary_v2_items`

The naming is deterministic. Overrides are allowed at DI registration time:

```csharp
services.AddSekibanDcbMaterializedView(opts =>
{
    opts.PhysicalNameResolver = (view, version, logical)
        => $"sekiban_mv_{SanitizeIdentifier(TenantContext.Current)}_{view}_v{version}_{logical}";
});
```

Any custom `PhysicalNameResolver` must return an already-sanitized identifier segment set (`[A-Za-z0-9_]` after normalization in the default implementation). The framework should validate or reject invalid names before embedding them in DDL.

### 5.2 MV registry and active tables

Two framework-owned tables track MV state. They are deliberately named with the `sekiban_mv_` prefix to distinguish from the existing `dcb_multi_projection_states` table:

```sql
CREATE TABLE sekiban_mv_registry (
    service_id               TEXT NOT NULL,
    view_name                TEXT NOT NULL,
    view_version             INT  NOT NULL,
    logical_table            TEXT NOT NULL,
    physical_table           TEXT NOT NULL,
    status                   TEXT NOT NULL,   -- initializing / catching_up / ready / active / retired
    current_position         TEXT,            -- last SortableUniqueId applied
    target_position          TEXT,            -- position at which catch-up is considered "ready"
    last_sortable_unique_id  TEXT,            -- used by dependency integrity checks
    last_updated             TIMESTAMPTZ NOT NULL,
    metadata                 JSONB,           -- future: dependency version map, operator metadata
    PRIMARY KEY (service_id, view_name, view_version, logical_table)
);

CREATE TABLE sekiban_mv_active (
    service_id     TEXT NOT NULL,
    view_name      TEXT NOT NULL,
    active_version INT  NOT NULL,
    activated_at   TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (service_id, view_name)
);
```

`service_id` mirrors the DCB convention (already present in `DbMultiProjectionState`) for multi-tenant isolation.

Note: `current_position` stores a `SortableUniqueId` string (DCB's existing ordering key), **not** a numeric offset. This aligns with `DbMultiProjectionState.LastSortableUniqueId`.

### 5.3 MV data tables

Data tables are authored entirely by the developer's `InitializeAsync`. The framework does not generate them. The only convention the framework **suggests** (not enforces) is the row metadata columns `_last_sortable_unique_id` and `_last_applied_at`.

---

## 6. Worked example

```csharp
public class OrderSummaryMvV1 : IMaterializedViewProjector
{
    public string ViewName => "OrderSummary";
    public int    ViewVersion => 1;

    public MvTable Orders { get; private set; } = default!;
    public MvTable Items  { get; private set; } = default!;

    public async Task InitializeAsync(IMvInitContext ctx)
    {
        Orders = ctx.RegisterTable("orders");
        Items  = ctx.RegisterTable("items");

        await ctx.ExecuteAsync($"""
            CREATE TABLE IF NOT EXISTS {Orders.PhysicalName} (
                id UUID PRIMARY KEY,
                status TEXT NOT NULL,
                total NUMERIC NOT NULL DEFAULT 0,
                created_at TIMESTAMPTZ NOT NULL,
                _last_sortable_unique_id TEXT NOT NULL,
                _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """);

        await ctx.ExecuteAsync($"""
            CREATE TABLE IF NOT EXISTS {Items.PhysicalName} (
                id UUID PRIMARY KEY,
                order_id UUID NOT NULL REFERENCES {Orders.PhysicalName}(id),
                product TEXT NOT NULL,
                qty INT NOT NULL,
                price NUMERIC NOT NULL,
                _last_sortable_unique_id TEXT NOT NULL,
                _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """);

        await ctx.ExecuteAsync($"""
            CREATE INDEX IF NOT EXISTS idx_{Items.PhysicalName}_order_id
            ON {Items.PhysicalName} (order_id)
            """);
    }

    // Read helpers (encapsulate SELECT statements for readability)

    private Task<OrderRow?> GetOrderByIdAsync(IMvApplyContext ctx, Guid orderId) =>
        ctx.QuerySingleOrDefaultAsync<OrderRow>(
            $"SELECT * FROM {Orders.PhysicalName} WHERE id = @Id",
            new { Id = orderId });

    // Write helpers (each returns an MvSqlStatement, never executes)

    private MvSqlStatement UpsertOrder(OrderRow order, string sid) =>
        new($"""
            INSERT INTO {Orders.PhysicalName}
                (id, status, total, created_at, _last_sortable_unique_id, _last_applied_at)
            VALUES (@Id, @Status, @Total, @CreatedAt, @SortableId, NOW())
            ON CONFLICT (id) DO UPDATE SET
                status                   = EXCLUDED.status,
                total                    = EXCLUDED.total,
                _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id,
                _last_applied_at         = EXCLUDED._last_applied_at
            WHERE {Orders.PhysicalName}._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id
            """, new {
                order.Id, order.Status, order.Total, order.CreatedAt,
                SortableId = sid
            });

    private MvSqlStatement InsertItem(OrderItemRow item, string sid) =>
        new($"""
            INSERT INTO {Items.PhysicalName}
                (id, order_id, product, qty, price, _last_sortable_unique_id, _last_applied_at)
            VALUES (@Id, @OrderId, @Product, @Qty, @Price, @SortableId, NOW())
            """, new {
                item.Id, item.OrderId, item.Product, item.Qty, item.Price,
                SortableId = sid
            });

    private MvSqlStatement UpdateOrderTotal(Guid orderId, decimal total, string sid) =>
        new($"""
            UPDATE {Orders.PhysicalName}
            SET total = @Total,
                _last_sortable_unique_id = @SortableId,
                _last_applied_at = NOW()
            WHERE id = @Id
              AND _last_sortable_unique_id < @SortableId
            """, new { Id = orderId, Total = total, SortableId = sid });

    // Apply: reads happen immediately, writes are returned

    public async Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(
        IEvent ev, IMvApplyContext ctx)
    {
        var sid = ctx.CurrentSortableUniqueId;

        switch (ev.Payload)
        {
            case OrderCreated c:
                return [UpsertOrder(
                    new OrderRow(c.OrderId, "Pending", 0m, c.Timestamp), sid)];

            case OrderItemAdded a:
                var order = await GetOrderByIdAsync(ctx, a.OrderId);
                if (order is null) return [];
                return [
                    InsertItem(new OrderItemRow(a.ItemId, a.OrderId, a.Product, a.Quantity, a.Price), sid),
                    UpdateOrderTotal(a.OrderId, order.Total + (a.Price * a.Quantity), sid)
                ];

            case OrderCancelled c:
                return [new($"""
                    UPDATE {Orders.PhysicalName}
                    SET status = 'Cancelled',
                        _last_sortable_unique_id = @SortableId,
                        _last_applied_at = NOW()
                    WHERE id = @Id
                    """, new { Id = c.OrderId, SortableId = sid })];

            default:
                return [];
        }
    }
}
```

Notice the POCOs used for reads are named `OrderRow`, `OrderItemRow` — avoiding clash with any existing domain `Order` type in sample projects. This is only a convention; users can name them however they like.

---

## 7. ORM layering (IMvRow, MvRowMapper)

### 7.1 Why we don't depend on Dapper in the public surface

Dapper is an excellent library and will be used **internally** in the initial Postgres implementation. But:

- The public authoring surface must not force callers, adapters, or future runtime-protocol implementations to depend on Dapper types
- Alternative backends (Npgsql direct, ADO.NET, native Postgres driver) should be pluggable
- WASM multi-language scenarios need an ABI-friendly row representation

We therefore introduce a small `IMvRow`/`IMvRowSet` abstraction that lives in `Sekiban.Dcb.MaterializedView`. Dapper becomes an implementation detail of the default `IMvApplyContext` realizer.

The names `IMvRow` / `IMvRowSet` deliberately carry the `Mv` prefix to avoid confusion with any existing `IRow`/`IRowSet` types in ADO.NET or third-party libraries.

### 7.2 `IMvRow` and `IMvRowSet`

```csharp
public interface IMvRow
{
    int ColumnCount { get; }
    IReadOnlyList<string> ColumnNames { get; }

    bool IsNull(string columnName);

    Guid           GetGuid(string columnName);
    string         GetString(string columnName);
    int            GetInt32(string columnName);
    long           GetInt64(string columnName);
    decimal        GetDecimal(string columnName);
    double         GetDouble(string columnName);
    bool           GetBoolean(string columnName);
    DateTimeOffset GetDateTimeOffset(string columnName);
    byte[]         GetBytes(string columnName);

    Guid?           GetGuidOrNull(string columnName);
    string?         GetStringOrNull(string columnName);
    int?            GetInt32OrNull(string columnName);
    decimal?        GetDecimalOrNull(string columnName);
    DateTimeOffset? GetDateTimeOffsetOrNull(string columnName);

    T GetAs<T>(string columnName);    // for JSONB, arrays, custom types

    string ToJson();                  // escape hatch / WASM marshaling
}

public interface IMvRowSet : IReadOnlyList<IMvRow>
{
    IReadOnlyList<string> ColumnNames { get; }
}
```

Column access is **by name only** — deliberately no positional accessors. This avoids the `object[] row; row[0]; row[1]` anti-pattern.

### 7.3 `MvRowMapper<T>` (convention-based default)

```csharp
public static class MvRowMapper<T> where T : class
{
    public static T MapFrom(IMvRow row);               // Expression-tree compiled, cached
    public static IReadOnlyList<T> MapAll(IMvRowSet set);
}
```

Convention: `snake_case` column → `PascalCase` property, with `[MvColumn("...")]` as an override. Implementation compiles to an `Expression<Func<IMvRow, T>>` once per type and caches it — reflection overhead only on first use. The mapper is expected to support both property-based population and record primary constructors / `init`-only members; a parameterless constructor is not required.

### 7.4 Extension methods on the context

```csharp
public static class MvApplyContextExtensions
{
    public static async Task<T?> QuerySingleOrDefaultAsync<T>(
        this IMvApplyContext ctx, string sql, object? param = null)
        where T : class
    {
        var row = await ctx.QuerySingleOrDefaultRowAsync(sql, param);
        return row is null ? null : MvRowMapper<T>.MapFrom(row);
    }
}
```

Developers who want full control drop down to `QuerySingleOrDefaultRowAsync` and map by hand. Developers who want zero ceremony use the typed `QuerySingleOrDefaultAsync<T>`. Developers who want zero-runtime-cost mapping use a future `[GeneratedMvRowMapping]` source generator.

### 7.5 Source generator (future, optional)

```csharp
[GeneratedMvRowMapping]
public partial record OrderRow(
    [property: MvColumn("id")]         Guid           Id,
    [property: MvColumn("status")]     string         Status,
    [property: MvColumn("total")]      decimal        Total,
    [property: MvColumn("created_at")] DateTimeOffset CreatedAt);
```

The generator emits a `public static OrderRow FromMvRow(IMvRow row)`. This is out of scope for the first PoC but should be feasible.

---

## 8. Runtime protocol / WASM considerations

### 8.1 Two layers, not one

This design intentionally separates two concerns:

1. **.NET authoring API**
   - `IMaterializedViewProjector`
   - `IMvInitContext`
   - `IMvApplyContext`
   - `MvSqlStatement`
2. **Language-neutral runtime protocol** (future)
   - serialized apply request / response messages
   - serialized read request / row result messages
   - serialized statement list messages

The .NET authoring API is optimized for maintainable .NET code. The future runtime protocol is optimized for Wasm/remote execution. They are related, but they are **not the same interface**.

### 8.2 The constraint

WebAssembly ABIs can only pass primitive types (`i32`, `i64`, `f32`, `f64`) and byte ranges (pointer + length). Complex C# types cannot cross the boundary directly; they must be serialized.

That means a Wasm or remote guest must never see `IDbConnection`, `IDbTransaction`, `object? param`, or `IEvent` directly. A host adapter converts between the runtime protocol and the host-side authoring API.

For this framework, the items that must cross the host ↔ guest boundary are therefore:

| Direction | Payload | Suggested form |
|---|---|---|
| guest → host | SQL string | UTF-8 bytes |
| guest → host | SQL parameters | JSON |
| host → guest | Event envelope to apply | JSON |
| host → guest | Row data (read result) | **JSON (PoC)** → MessagePack (opt) |
| guest → host | `MvSqlStatement` list | JSON |

### 8.3 Cost analysis

Per-event marshaling cost estimate with JSON:

| Payload | Count per event | JSON cost |
|---|---|---|
| Event | 1 | ~5-10μs |
| Row reads | 1-2 | ~10-20μs |
| Writes (`MvSqlStatement` list) | 1-3 | ~5-10μs |
| **Total** | | **~20-40μs per event** |

Throughput impact:

| Target | JSON marshaling / sec | Note |
|---|---|---|
| 1,000 events/sec | 20-40ms (2-4%) | Negligible |
| 10,000 events/sec | 200-400ms (20-40%) | Usable but felt |
| 100,000 events/sec | 2-4s (200%+) | MessagePack needed |

**Conclusion**: JSON is sufficient for PoC and for most operational workloads. Extreme catch-up throughput should either use MessagePack at the boundary or stay on the native (non-WASM) path entirely.

### 8.4 Key observation

**SQL strings and parameters have effectively zero marshaling overhead** — they're UTF-8 strings that pass as `(ptr, len)`. The only expensive direction is row data coming back from reads. This is a well-bounded optimization target.

### 8.5 Decimal handling

`decimal` through JSON loses precision. Options, in order of preference:

1. **String-encoded** (`"1234.5678"`) — safe, universal, slightly more parsing
2. MessagePack extension — binary efficient
3. Custom binary — overkill for PoC

For PoC, always encode `decimal` as a string at the ABI boundary.

---

## 9. Relationship to existing DCB components

### 9.1 `IEventStore`

No changes required. The new MV catch-up worker reads events via `ReadAllSerializableEventsAsync(since: currentPosition, maxCount: batchSize)` and feeds successful results to `ApplyToViewAsync`. Read failures remain part of the worker contract because the underlying API returns `ResultBox<IEnumerable<SerializableEvent>>`.

### 9.2 `SortableUniqueId`

Used directly as `current_position` in the MV registry. The string representation is already suitable for comparison (`<`, `<=`, `>`, `>=`). `CurrentSortableUniqueId` in `IMvApplyContext` is the event's `SortableUniqueId.Value`.

### 9.3 `SafeUnsafeProjectionState` / safe window

The MV model does **not** use the in-memory safe/unsafe state directly. Instead, we have two options for handling the safe window:

**Option A (recommended for PoC): delay-apply**
- Only apply events whose `SortableUniqueId` age is beyond the safe window (5s default)
- Track two positions: `safe_position` (committed to disk) and `tentative_position` (read-ahead, not persisted)
- On restart, always resume from `safe_position`

**Option B: apply-immediately, rollback-on-reorder**
- Apply everything as it arrives, but track a rollback point
- Much more complex, defer to later phase

The PoC uses Option A. This matches the semantics of `DbMultiProjectionState.SafeWindowThreshold`.

### 9.4 `GeneralMultiProjectionActor` / Orleans

MVs do **not** run inside `GeneralMultiProjectionActor`. They run in a separate catch-up worker called `MvCatchUpWorker`. This worker can be:

- A hosted service (IHostedService) in a .NET host
- A dedicated Orleans grain per view (future)
- An external process reading events from the event store

The PoC starts with a simple IHostedService-based worker. Orleans integration is a later phase.

### 9.5 `MultiProjectionStateBuilder` / `IMultiProjectionStateStore`

**Not touched.** MVs have their own orchestration via `MvCatchUpWorker` and `IMvRegistryStore`. The two systems are parallel — they do not share persistence code.

### 9.6 `Sekiban.Dcb.Postgres`

`Sekiban.Dcb.Postgres` stays exactly as it is (snapshot-based multi-projection store). A new package, `Sekiban.Dcb.MaterializedView` (core), plus `Sekiban.Dcb.MaterializedView.Postgres` (implementation), adds MVs without touching the existing code.

### 9.7 Name-collision audit

| Existing DCB type | New MV type | Collision? |
|---|---|---|
| `ICoreMultiProjector<T>` | `IMaterializedViewProjector` | No — different name, different interface style |
| `MultiProjectionStateBuilder` | `MvCatchUpWorker` | No |
| `MultiProjectionStateRecord` | `MvRegistryEntry` | No |
| `MultiProjectionStateWriteRequest` | (not needed) | — |
| `DbMultiProjectionState` | `sekiban_mv_registry` + `sekiban_mv_active` | No — different tables, different prefix |
| `IMultiProjectionStateStore` | `IMvRegistryStore` + `IMvExecutor` | No |
| `Sekiban.Dcb.Postgres` | `Sekiban.Dcb.MaterializedView.Postgres` | No |
| `SafeProjection<T>` | (not used by MVs) | — |
| `SafeUnsafeProjectionState<K,V>` | (not used by MVs) | — |
| `IEvent`, `SortableUniqueId`, `IEventStore`, `ServiceId` | (shared) | Shared intentionally |

Every publicly exposed MV type has either:
- A unique prefix (`Mv*`)
- The word `MaterializedView` in full
- Or is a shared DCB primitive that both sides agree on (`IEvent`, `SortableUniqueId`)

This guarantees code search like "grep -rn 'MultiProjector'" returns only the existing multi-projection ecosystem, and "grep -rn 'MaterializedView'" or "grep -rn 'Mv'" returns only the new ecosystem.

---

## 10. Version / catch-up lifecycle

### 10.1 State machine per (view, version)

```
   [new]
     │ (developer adds a new version)
     ▼
[initializing]  ← CREATE TABLE/INDEX; register in sekiban_mv_registry
     │
     ▼
[catching_up]   ← MvCatchUpWorker reads events and applies them
     │            (current_position advances)
     │
     │ current_position reaches safe window horizon
     ▼
   [ready]      ← caught up, not yet serving queries
     │
     │ operator activates (or auto-activate)
     ▼
  [active]      ← sekiban_mv_active points here; reads hit this version
     │
     │ new version becomes active; old version retires
     ▼
  [retired]     ← no longer serving queries; still on disk
     │
     │ cleanup (manual or TTL-based)
     ▼
  [deleted]     ← physical tables dropped, registry rows removed
```

### 10.2 Worker loop (pseudo-code)

```csharp
while (!cancellationToken.IsCancellationRequested)
{
    var readResult = await eventStore.ReadAllSerializableEventsAsync(
        since: currentPosition, maxCount: BatchSize);
    if (!readResult.IsSuccess)
    {
        if (readResult.GetException() is NotSupportedException unsupported)
        {
            logger.LogError(
                unsupported,
                "ReadAllSerializableEventsAsync is not supported by the configured event store. Stopping catch-up worker for {ViewName}/{ViewVersion}.",
                viewName, viewVersion);
            break;
        }

        logger.LogWarning(
            readResult.GetException(),
            "Failed to read events for {ViewName}/{ViewVersion}. Retrying after delay.",
            viewName, viewVersion);
        await Task.Delay(pollInterval, cancellationToken);
        continue;
    }

    var batch = readResult.GetValue().ToList();
    if (batch.Count == 0) { await Task.Delay(pollInterval, cancellationToken); continue; }

    foreach (var ev in batch)
    {
        if (!IsInsideSafeWindow(ev.SortableUniqueId, safeThreshold))
        {
            // Still inside unsafe window — defer
            break;
        }

        using var tx = connection.BeginTransaction();
        var dependencyMap = await registry.GetPinnedDependencyMapAsync(
            viewName, viewVersion, tx);
        var applyContext = new PostgresMvApplyContext(
            connection, tx, ev.ToEvent(), dependencyMap);
        var statements = await view.ApplyToViewAsync(ev.ToEvent(), applyContext);
        foreach (var stmt in statements)
            await connection.ExecuteAsync(stmt.Sql, stmt.Parameters, tx);
        await registry.UpdatePositionAsync(
            viewName, viewVersion, ev.SortableUniqueId, tx); // updates all logical_table rows for the view/version
        tx.Commit();

        currentPosition = ev.SortableUniqueId;
    }
}
```

### 10.3 Activation and rollback

Activation is an atomic update of `sekiban_mv_active.active_version`. When dependency-aware views are introduced, activation also captures the dependency version map that the new active version will read against. Optionally, a view-based read side (§11) allows activation to also `CREATE OR REPLACE VIEW` so external consumers see the switch atomically.

Rollback from v2 to v1 is equally simple: update `active_version` back. Both versions' data tables remain intact until the operator issues `RETIRE`.

---

## 11. Read side (future)

The PoC ships without a query API. Queries go through `Connection` directly using physical table names obtained from the registry. Subsequent work adds:

- **Pattern A — typed query API**: `mvQuery.For("OrderSummary").Table("orders").WhereAsync<OrderRow>(...)`
- **Pattern B — raw SQL with `{logical_table}` placeholders**
- **Pattern C — automatic `CREATE OR REPLACE VIEW sekiban_mv_view_*` generation** so BI tools can use the logical name transparently

These are in scope for a later phase.

---

## 12. Integration with DCB's `ServiceId`

All registry and active entries are scoped by `service_id`. The default resolver reads from `IServiceIdProvider` (an existing DCB abstraction) so that multi-tenant deployments work without change. Physical table names can optionally include the service id via a custom resolver.

---

## 13. Error handling

- SQL execution failures inside a transaction: transaction rolls back; `current_position` is NOT advanced; the event will be retried on next worker iteration
- Non-idempotent writes: mitigated by `_last_sortable_unique_id` guards in user-written SQL
- Schema mismatch (old code against new table): `status` stays in `initializing` until `InitializeAsync` succeeds
- Poison events (event that always throws in `ApplyToViewAsync`): future work — for PoC, log and halt the worker

---

## 14. Testing strategy

1. **Unit tests for `ApplyToViewAsync`**: purely check the returned `MvSqlStatement` list
2. **Integration tests with Testcontainers for Postgres**: real database, ensure SQL is valid and results are correct
3. **Cross-model parity tests**: a sample MV and a sample `ICoreMultiProjector<T>` fed the same events should converge to semantically equivalent read results (keys present in both, same totals, etc.)
4. **Idempotency tests**: replay same events twice, assert final state is identical
5. **Version switch tests**: v1 active → v2 catches up → activate v2 → validate read-side continuity

---

## 15. Why this design is right for DCB

- **Parallel, not replacing**: zero risk to existing snapshot-based multi-projections
- **Minimal public surface**: 3 interfaces + 1 value type + optional IMvRow/MvRowMapper
- **No dialect abstraction leak**: developer owns SQL, framework owns orchestration
- **Reuses DCB primitives**: `IEventStore`, `SortableUniqueId`, `ServiceId`, safe window
- **Name-clash-free**: every new public type uses `MaterializedView` / `Mv*` naming
- **Runtime-protocol-ready**: the future Wasm/remote boundary is explicitly separate from the .NET authoring API
- **Testable**: `ApplyToViewAsync` is pure enough to unit-test against the returned statement list
- **AI-convertible**: existing `ICoreMultiProjector<T>` code can be mechanically translated (see `integration-notes.md`)

## 16. What this design deliberately avoids

- Rich `ISqlOperation` / `IDbDialect` abstractions (rejected: leaky, expensive)
- `Insert/Update/Upsert` methods on `MvTable` (rejected: SQL dialect differences)
- Entity Framework integration (rejected: migration model conflicts with multi-version coexistence)
- Query DSL on the read side (deferred: out of PoC scope)
- Cross-view writes (forbidden: would break isolation guarantees)
- Automatic schema diffing / auto-migration (deferred: DDL authorship stays with the developer for now)
- Reusing any existing DCB type name (rejected: `MaterializedView` / `Mv*` is the unified prefix)

## 17. Open questions

See `open-questions.md`.
