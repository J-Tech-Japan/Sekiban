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
