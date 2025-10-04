# Orleans Setup - Hosting DCB on Actors

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
> - [Orleans Setup](10_orleans_setup.md) (You are here)
> - [Storage Providers](11_dapr_setup.md)
> - [Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)

DCB uses Orleans grains to implement TagConsistent actors, TagState caches, and MultiProjections. The sample AppHost
configures everything via `.UseOrleans` (`internalUsages/DcbOrleans.ApiService/Program.cs`).

## Cluster Configuration

```csharp
builder.UseOrleans(config =>
{
    if (builder.Environment.IsDevelopment())
    {
        config.UseLocalhostClustering();
    }
    else if (useCosmosClustering)
    {
        config.UseCosmosClustering(options =>
        {
            options.ConfigureCosmosClient(connectionString);
        });
    }

    config.Configure<ClusterOptions>(options =>
    {
        options.ClusterId = "sekiban-dcb";
        options.ServiceId = "sekiban-dcb-service";
    });
});
```

## Storage Providers

- **Grain Storage** – Choose Blob, Table, or Cosmos via config (`ORLEANS_GRAIN_DEFAULT_TYPE`).
- **TagState** – `TagStateGrain` persists optional snapshots using the configured grain storage.
- **MultiProjection Snapshots** – Provide `IBlobStorageSnapshotAccessor` (see Storage Providers guide).

## Streams

DCB relies on Orleans streams to deliver events to projections and downstream consumers.

- Development: In-memory streams with high-frequency polling for fast feedback.
- Production: Azure Queue streams with partitioned queues (`dcborleans-eventstreamprovider-0..2`).

```csharp
config.AddAzureQueueStreams("EventStreamProvider", configurator =>
{
    configurator.ConfigureAzureQueue(options =>
    {
        options.QueueServiceClient = sp.GetKeyedService<QueueServiceClient>("DcbOrleansQueue");
        options.QueueNames = ["dcborleans-eventstreamprovider-0", "-1", "-2"];
    });
    configurator.ConfigureCacheSize(8192);
});
```

A second Azure Queue stream (`"DcbOrleansQueue"`) carries integration events from the `OrleansEventPublisher`.

## Grain Implementations

- `TagConsistentGrain` wraps `GeneralTagConsistentActor` to manage reservations
  (`src/Sekiban.Dcb.Orleans/Grains/TagConsistentGrain.cs`).
- `TagStateGrain` wraps `GeneralTagStateActor` for cached projections
  (`src/Sekiban.Dcb.Orleans/Grains/TagStateGrain.cs`).
- `MultiProjectionGrain` processes event streams and serves queries
  (`src/Sekiban.Dcb.Orleans/Grains/MultiProjectionGrain.cs`).

## Executor Registration

```csharp
builder.Services.AddSingleton<ISekibanExecutor, OrleansDcbExecutor>();
```

The executor uses `OrleansActorObjectAccessor` to locate grain instances on demand (`src/Sekiban.Dcb.Orleans/OrleansActorObjectAccessor.cs`).

## ASP.NET Integration

`AddServiceDefaults()` wires telemetry, health checks, and Aspire instrumentation so Orleans metrics show up in the
Dashboard. Add `app.MapHealthChecks("/health")` for readiness probes.

## Deployment Considerations

- Set `ORLEANS_CLUSTERING_TYPE` to `azuretable`, `cosmos`, or leave empty for development.
- Pre-create Azure Queues in production or enable `IsResourceCreationEnabled` during provisioning.
- Scale out silos horizontally; Orleans handles tag grain placement automatically.

## Testing Without Orleans

Use `InMemorySekibanExecutor` (`src/Sekiban.Dcb/InMemory/InMemorySekibanExecutor.cs`) to run commands locally without a
silo. This executor spins in-process actors and stores events in memory—perfect for unit tests.
