# MultiProjection - Composable Read Models

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Commands, Events, Tags, Projectors](03_aggregate_command_events.md)
> - [MultiProjection](04_multiple_aggregate_projector.md) (You are here)
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
> - [Materialized View Basics](20_materialized_view.md)

While tag projectors keep per-tag state, MultiProjection composes those states into application-specific read models.
Each MultiProjection runs in its own Orleans grain (or actor) and can offload large snapshots to Azure Blob Storage.

## Anatomy of a MultiProjection

Implement `IMultiProjector<T>` and describe how tag events roll up into projection state.

```csharp
public class WeatherForecastProjection : IMultiProjector<WeatherForecastProjection>
{
    public static string MultiProjectorName => "WeatherForecast";
    public static string MultiProjectorVersion => "1.0.0";

    public static MultiProjectionState Project(
        MultiProjectionState current,
        Event currentEvent,
        IReadOnlyDictionary<ITag, TagState> tagStates)
    {
        // Access tag states that were touched by the event
        // Combine them into a projection payload
    }
}
// Source: internalUsages/Dcb.Domain/Projections/WeatherForecastProjection.cs
```

Helpers like `GenericTagMultiProjector<TProjector, TTag>` let you generate list-style projections without bespoke code.
The sample domain registers multiple generic projectors in `internalUsages/Dcb.Domain/DomainType.cs`.

## State Lifecycle

1. Tag events arrive through Orleans streams or polling.
2. The MultiProjection grain requests latest tag states from `TagStateGrain`.
3. Projection state is updated in memory and optionally offloaded via `IBlobStorageSnapshotAccessor`.
4. Queries read from the MultiProjection grain, which can enforce `WaitForSortableUniqueId` semantics for fresh data.

`src/Sekiban.Dcb.Orleans/Grains/MultiProjectionGrain.cs` contains the orchestrator that wires these pieces together.

## Snapshot Offloading

Large projections can use `Sekiban.Dcb.BlobStorage.AzureStorage` to persist snapshots in Azure Blob Storage.
Register an accessor:

```csharp
services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
    new AzureBlobStorageSnapshotAccessor(
        sp.GetRequiredKeyedService<BlobServiceClient>("MultiProjectionOffload"),
        "multiprojection-snapshots"));
```

The Orleans grain detects the accessor and periodically checkpoints state, reducing silo memory usage
(`src/Sekiban.Dcb.Orleans/Grains/MultiProjectionGrainState.cs`).

## Consistency Considerations

- MultiProjection receives events in global order; use `WaitForSortableUniqueId` on queries to avoid stale reads.
- Because tag states are cached, projector code must be deterministic and side-effect free.
- Projection version changes trigger a rebuild. Bump `MultiProjectorVersion` whenever schema or logic changes.

## Practical Use Cases

- Aggregated dashboards (counts, availability, leaderboards)
- Materialized list views for Blazor components
- Cross-tag joins without hitting the primary event store

Refer to `internalUsages/Dcb.Domain/Student/StudentSummaries.cs` for a concise example of projecting multiple tags into a
domain-specific summary list.

## MultiProjection vs. Materialized View

Sekiban now supports two different read-model styles:

- **MultiProjection**: In-memory projection state hosted by Orleans grains. Best when the read model is naturally consumed
  through `ISekibanExecutor.QueryAsync`.
- **Materialized View**: Database tables updated from the same ordered event stream. Best when you need SQL paging,
  filtering, reporting, or direct table access from external tooling.

Use MultiProjection when you want the simplest end-to-end Sekiban query path. Use materialized views when the read model
must live in a relational database. See [Materialized View Basics](20_materialized_view.md).

## Dual-State Convergence & Safe-Window Graduation (SEK-G18)

Multi-projections keep two states: a **safe** state (events older than the safe window,
in global `SortableUniqueId` order) and a **served/unsafe** state (what queries return).

- **Served state is reconciled, not arrival-ordered.** At every safe-window graduation the
  served state is re-derived as `safe baseline + still-buffered events replayed in global
  SortableUniqueId order`, then published atomically. Two events that arrive out of order
  (e.g. a cross-instance duplicate create) therefore converge to the same result on every
  instance — the globally-earliest event wins for a first-event-wins projector, regardless
  of local arrival order.
- **`IsSafeState` is truthful.** It is `true` only when the served state was published
  identical to the safe state (no buffered events remain, no rebuild pending) — never from
  a timestamp comparison alone. A query that returns `IsSafeState=true` is guaranteed to be
  the reconciled, globally-ordered value.
- **Ordering-regression rebuild (fail-closed).** If an event promotes to safe out of global
  order versus the held safe head, the incremental (compacted-baseline) path cannot reorder
  it. The projection then performs a **full ordered rebuild from the authoritative event
  store** from the initial state; while rebuilding, every state/scalar/list query awaits the
  rebuild barrier and answers with the rebuilt payload or fails closed — it never returns a
  stale success. The G14 fault path is reserved for failures OF the rebuild itself.

### Checkpoint-restore exactness (SEK-G18 / #1086)

- **Catch-up start is authoritative.** After a checkpoint restore, catch-up starts from the
  checkpoint record's `LastSortableUniqueId`, read **exclusive** of that position — an event
  whose id equals the checkpoint position is already reflected in the restored payload and is
  not re-read, so it is never double-counted or re-folded. (All event stores —
  Postgres/SQLite/Cosmos/DynamoDB, the in-memory store, and the Hybrid cold→hot handoff — use
  a strict `SortableUniqueId > since` filter.)
- **`EventsProcessed` is a durable safe-checkpoint count** used as an integrity signal:
  restore takes it as the baseline; a restart that writes zero new events restores exactly the
  same payload/position/threshold/count.
