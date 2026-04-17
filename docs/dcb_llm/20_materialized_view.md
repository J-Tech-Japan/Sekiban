# Materialized View Basics

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Commands, Events, Tags, Projectors](03_aggregate_command_events.md)
> - [MultiProjection](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Command Workflow](06_workflow.md)
> - [Serialization & Domain Types](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md)
> - [Client UI (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md)
> - [Storage Providers](11_storage_providers.md)
> - [Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)
> - [Cold Events and Catch-up](19_cold_events.md)
> - [Materialized View Basics](20_materialized_view.md) (You are here)
> - [Unsafe Window Materialized View](21_unsafe_window_materialized_view.md)

Materialized views are database-backed read models for DCB. Instead of keeping the read model only in Orleans memory,
Sekiban can apply the ordered event stream into SQL tables and let the application query those tables directly.

## What Problem Does It Solve?

MultiProjection is still the default read-model path in DCB. Materialized views are useful when you need:

- SQL paging, filtering, and sorting over large lists
- direct table access from dashboards, BI tools, or external services
- a relational representation that can be indexed independently from the event store
- a read model that survives Orleans grain deactivation without snapshot-specific query code

Use MultiProjection when reads are fully inside Sekiban. Use materialized views when the read side must be exposed as a
database.

## Runtime Shape

The current runtime is split into provider-neutral core plus provider packages:

- `Sekiban.Dcb.MaterializedView`
  Core contracts such as `IMaterializedViewProjector`, `IMvInitContext`, `IMvApplyContext`, `MvRegistryEntry`, and the
  catch-up worker.
- `Sekiban.Dcb.MaterializedView.Postgres`
  PostgreSQL implementation for table registration, row access, registry persistence, and event application.
- `Sekiban.Dcb.MaterializedView.SqlServer`
  SQL Server implementation for registry persistence and ordered event application.
- `Sekiban.Dcb.MaterializedView.MySql`
  MySQL implementation for registry persistence and ordered event application.
- `Sekiban.Dcb.MaterializedView.Sqlite`
  SQLite implementation for registry persistence and ordered event application.
- `Sekiban.Dcb.MaterializedView.Orleans`
  Orleans grain orchestration, startup activation, and `IMvOrleansQueryAccessor`.

The event source of truth is still the DCB event store. Materialized views are downstream projections from that store.

## High-Level Flow

1. A DCB command writes an event to the global event store.
2. The materialized view runtime reads ordered events from the store.
3. A projector translates each event into SQL statements.
4. The registry records the current catch-up position and active version.
5. Orleans coordinates stream delivery, buffering, and refresh.
6. The application queries the resulting database table.

This means correctness still depends on ordered event application, not on ad-hoc SQL updates.

## Registering the Runtime

Typical registration in an Orleans host looks like this:

```csharp
builder.Services.AddSekibanDcbMaterializedView(options =>
{
    options.BatchSize = 100;
    options.PollInterval = TimeSpan.FromSeconds(1);
});

builder.Services.AddMaterializedView<WeatherForecastMvV1>();

builder.Services.AddSekibanDcbMaterializedViewPostgres(
    builder.Configuration,
    connectionStringName: "DcbMaterializedViewPostgres",
    registerHostedWorker: false);

builder.Services.AddSekibanDcbMaterializedViewOrleans();
```

Source: `internalUsages/DcbOrleans.WithoutResult.ApiService/Program.cs`

Notes:

- `AddSekibanDcbMaterializedView` registers shared options.
- `AddMaterializedView<TView>` registers one projector.
- `AddSekibanDcbMaterializedViewPostgres` wires the registry and executor.
- `AddSekibanDcbMaterializedViewSqlServer`, `AddSekibanDcbMaterializedViewMySql`, and `AddSekibanDcbMaterializedViewSqlite`
  provide the same classic MV runtime for their respective databases.
- `AddSekibanDcbMaterializedViewOrleans` adds Orleans-side activation and query access.

Provider-specific registration examples:

```csharp
builder.Services.AddSekibanDcbMaterializedViewSqlServer(configuration, "DcbMaterializedViewSqlServer");
builder.Services.AddSekibanDcbMaterializedViewMySql(configuration, "DcbMaterializedViewMySql");
builder.Services.AddSekibanDcbMaterializedViewSqlite(configuration, "DcbMaterializedViewSqlite");
```

Projectors still emit SQL directly. For portable projectors, branch on `ctx.DatabaseType` and emit the SQL dialect that
matches the selected provider. Unsafe Window MV remains PostgreSQL-only in v1.

## Writing a Projector

A materialized view projector implements `IMaterializedViewProjector`.

```csharp
public sealed class WeatherForecastMvV1 : IMaterializedViewProjector
{
    public string ViewName => "WeatherForecast";
    public int ViewVersion => 1;

    public MvTable Forecasts { get; private set; } = default!;

    public async Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default)
    {
        Forecasts = ctx.RegisterTable("forecasts");
        await ctx.ExecuteAsync($"""
            CREATE TABLE IF NOT EXISTS {Forecasts.PhysicalName} (
                forecast_id UUID PRIMARY KEY,
                location TEXT NOT NULL,
                forecast_date DATE NOT NULL,
                temperature_c INT NOT NULL,
                summary TEXT NULL,
                is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                _last_sortable_unique_id TEXT NOT NULL,
                _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """, cancellationToken: cancellationToken);
    }

    public Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(
        Event ev,
        IMvApplyContext ctx,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MvSqlStatement>>([]);
}
```

Source: `internalUsages/Dcb.Domain.WithoutResult/MaterializedViews/WeatherForecastMvV1.cs`

Projector responsibilities:

- `InitializeAsync`
  Register logical tables and issue `CREATE TABLE` / `CREATE INDEX` statements.
- `ApplyToViewAsync`
  Translate one event into one or more SQL statements.

## Idempotency and Ordering

Materialized views must be safe to replay. The usual pattern is:

- keep `_last_sortable_unique_id` on every row
- update a row only when the incoming sortable id is newer
- treat the event store ordering as the source of truth

Example:

```sql
UPDATE some_table
SET value = @Value,
    _last_sortable_unique_id = @SortableUniqueId
WHERE id = @Id
  AND _last_sortable_unique_id < @SortableUniqueId;
```

This lets catch-up and live stream delivery converge on the same final state.

## Materialized View Registry

The runtime stores operational metadata per logical table:

- service id
- view name and active version
- logical table name and resolved physical table
- current position / last sortable unique id
- applied event version count
- last stream-applied and catch-up-applied sortable ids

This metadata is used to:

- discover the active physical table
- report status to operators
- decide whether a view is catching up, ready, or active

## Querying the Tables

Applications should not hardcode the physical table name. Use `IMvOrleansQueryAccessor` to resolve it.

```csharp
var context = await mvQueryAccessor.GetAsync(projector);
var forecastEntry = context.GetRequiredTable("forecasts");

await using var connection = new NpgsqlConnection(context.ConnectionString);
await connection.OpenAsync();

var rows = await connection.QueryAsync<WeatherForecastMvRow>(
    $"SELECT * FROM {forecastEntry.PhysicalTable} WHERE is_deleted = FALSE");
```

The query context contains:

- `DatabaseType`
- `ConnectionString`
- `Entries`
- `Grain`

The grain can also be used to check status or wait until a given sortable id has been received.

## Materialized View vs. MultiProjection

| Aspect | MultiProjection | Materialized View |
| --- | --- | --- |
| Storage | Orleans grain state | SQL tables |
| Read path | `ISekibanExecutor.QueryAsync` | SQL / Dapper / database access |
| Best for | application-internal read models | list views, reporting, external consumers |
| Freshness control | `WaitForSortableUniqueId` | grain status + SQL reads |
| Schema ownership | projection payload | explicit table DDL |

They are complementary. A service can use both.

## Current Scope

Current implementation status:

- database backend for materialized views: PostgreSQL
- orchestration host: Orleans
- event source: existing DCB event store

The sample application in `internalUsages/DcbOrleans.WithoutResult.ApiService` uses:

- DCB event store in Postgres
- materialized view tables in a separate Postgres connection
- Orleans grain orchestration for status, buffering, and refresh

## Practical Guidance

- start with one projector and one logical table
- keep the row schema explicit and simple
- always store `_last_sortable_unique_id`
- use indexes for the query shape you actually expose
- bump `ViewVersion` when table schema or projection logic changes
- do not treat the materialized view as the source of truth; rebuilds must remain possible

## Related Reading

- [MultiProjection](04_multiple_aggregate_projector.md)
- [Query](05_query.md)
- [Storage Providers](11_storage_providers.md)
- `internalUsages/Dcb.Domain.WithoutResult/MaterializedViews/WeatherForecastMvV1.cs`
- `internalUsages/DcbOrleans.WithoutResult.ApiService/Program.cs`
