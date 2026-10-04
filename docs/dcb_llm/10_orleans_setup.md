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
> - [Storage Providers](11_storage_providers.md)
> - [Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)
> - [Durable Orleans Subscriptions](23_durable_orleans_subscriptions.md)

DCB uses Orleans grains to implement TagConsistent actors, TagState caches, and MultiProjections. The sample AppHost
configures everything via `.UseOrleans` (`internalUsages/DcbOrleans.ApiService/Program.cs`).

## Supported Environments

| Environment | Clustering | Grain Storage | Streams |
|-------------|------------|---------------|---------|
| **Development (Aspire)** | Localhost | Memory | Memory |
| **Azure** | Cosmos DB / Azure Table | Azure Blob | Azure Queue |
| **AWS** | RDS PostgreSQL (ADO.NET) | Memory / Custom | Amazon SQS |

---

## Azure Cluster Configuration

```csharp
builder.UseOrleans(config =>
{
    if (builder.Environment.IsDevelopment())
    {
        config.UseLocalhostClustering();
    }
    else
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

---

## AWS Cluster Configuration

AWS uses RDS PostgreSQL for Orleans clustering and SQS for streams.

```csharp
builder.UseOrleans(config =>
{
    if (builder.Environment.IsDevelopment())
    {
        config.UseLocalhostClustering();
        config.AddMemoryStreams("EventStreamProvider");
    }
    else
    {
        // RDS PostgreSQL clustering
        var rdsConnectionString = BuildRdsConnectionString();
        config.UseAdoNetClustering(options =>
        {
            options.Invariant = "Npgsql";
            options.ConnectionString = rdsConnectionString;
        });
        config.UseAdoNetReminderService(options =>
        {
            options.Invariant = "Npgsql";
            options.ConnectionString = rdsConnectionString;
        });

        // SQS streams
        config.AddSqsStreams("EventStreamProvider", configurator =>
        {
            configurator.ConfigureSqs(options =>
            {
                options.Region = "ap-northeast-1";
                options.QueuePrefix = "orleans-stream-prod";
            });
        });
    }

    config.Configure<ClusterOptions>(opt =>
    {
        opt.ClusterId = "sekiban-dcb";
        opt.ServiceId = "sekiban-service";
    });
});
```

### Orleans Schema Initialization (AWS)

RDS PostgreSQL requires Orleans schema tables. Use `OrleansSchemaInitializer` to create them at startup.

```csharp
var schemaInitializer = new OrleansSchemaInitializer(logger);
await schemaInitializer.InitializeAsync(rdsConnectionString);
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

### Azure
- Set `ORLEANS_CLUSTERING_TYPE` to `azuretable` or `cosmos`
- Pre-create Azure Queues or enable `IsResourceCreationEnabled` during provisioning

### AWS
- RDS PostgreSQL schema is auto-created by `OrleansSchemaInitializer`
- SQS queues are provisioned via CDK
- Set `Orleans__ClusterId` and `Orleans__ServiceId` via environment variables

### Common
- Scale out silos horizontally; Orleans handles tag grain placement automatically

## Grain directory and duplicate activations

Orleans 10.3.1 defaults to the eventually consistent `LocalGrainDirectory`. Sekiban's hosts and templates do not
configure a grain directory, so they use this default. Membership churn can briefly produce duplicate activations;
Orleans resolves directory duplicates by deactivating the duplicate, but the overlap matters for writes. Azure Table
or Cosmos **clustering** configures membership, not the grain directory or an event-store fence.
See the [Orleans grain directory guide](https://learn.microsoft.com/en-us/dotnet/orleans/host/grain-directory).

### Experimental strongly consistent directory (opt-in)

Consider the strongly consistent in-cluster directory: it lowers the probability of competing writes for `TagConsistentGrain`
and `MaterializedViewGrain`. In 10.3.1, `AddDistributedGrainDirectory` is an experimental extension in
`Orleans.Hosting.CoreHostingExtensions` (`Microsoft.Orleans.Runtime`), marked `ORLEANSEXP003`.
It is an opt-in, not a template or drop-in production default. With no name, it becomes the cluster-wide default:

```csharp
using Orleans.Hosting;

#pragma warning disable ORLEANSEXP003
siloBuilder.AddDistributedGrainDirectory(); // experimental; cluster-wide default
#pragma warning restore ORLEANSEXP003
```

Alternatively, register a named directory and select it on a grain implementation class with
`[GrainDirectory("ConsistencyDirectory")]`:

```csharp
#pragma warning disable ORLEANSEXP003
siloBuilder.AddDistributedGrainDirectory("ConsistencyDirectory");
#pragma warning restore ORLEANSEXP003
```

Import `Orleans.GrainDirectory` for the attribute. It belongs on the grain class, not its interface. Applying this
per-type option to Sekiban's built-in grains requires changing their implementation classes; host registration alone
does not select the named directory for them.
See the [10.3.1 registration API source](https://github.com/dotnet/orleans/blob/v10.3.1/src/Orleans.Runtime/Hosting/CoreHostingExtensions.cs).
After an ungraceful failure, recovery uses range leases (`RangeLeaseDuration`, 30 seconds). Plan for this recovery delay.

### External directories and the remaining boundary

Redis, Azure Table and ADO.NET directories own their consistency. They can be registered per type or as the default
(for example, `UseRedisGrainDirectoryAsDefault` or `UseAzureTableGrainDirectoryAsDefault`, with the corresponding
provider package and options). They are not interchangeable correctness guarantees: the Azure Table implementation has
a registration race, and Redis `EntryExpiry` can cause duplicate activations.

No directory fences a silo that has been declared dead but is still running. Orleans terminates that process only
when it learns its dead status; until then it may still serve an activation through a co-hosted API, cached routes or
in-flight calls. A stronger directory narrows races; a storage-side conditional check is still the final fence.
See [Orleans cluster management](https://learn.microsoft.com/en-us/dotnet/orleans/implementation/cluster-management).

- Switch the whole cluster rather than mixing directory configurations in a rolling upgrade. A rolling switch is
  undocumented; prepare and test a rollout and rollback plan before adopting the experimental directory.
- Tune membership and failure detection for the deployment, balancing prompt detection with false suspicions, and
  terminate stale silos. Monitor membership churn and make sure the host infrastructure restarts terminated processes.
- Choose storage protection using [fast and strict tag consistency](13_common_issues.md#fast-and-strict-tag-consistency),
  including provider availability, costs and the boundary for already-running commands.

### Sekiban surfaces under duplicate activation

| Surface | Residual risk and protection |
|---------|------------------------------|
| `TagConsistentGrain` / `GeneralTagConsistentActor` | Correctness risk: reservations and cached tag heads are per activation. Two activations can both reserve the same expected head and append on the default write path. Opt into the PostgreSQL [derived fence](11_storage_providers.md#derived-fence-tagconsistencyfenceoptions) to durably compare read reservation inputs across activations; unread tags remain unfenced. |
| `MultiProjectionGrain` checkpoints | Protected by [SEK-G20 generation-aware checkpoint CAS](11_storage_providers.md#sek-g20-generation-aware-checkpoint-cas) on CAS-capable stores (InMemory, SQLite, DynamoDB, PostgreSQL, Cosmos). Event-ID de-duplication protects replay; adopting a checkpoint can require extra catch-up. Custom stores with unconditional writes do not have this CAS protection. |
| `MaterializedViewGrain` | Duplicate activation or redelivery can double-apply non-idempotent SQL. Use [idempotent projector SQL](20_materialized_view.md#idempotency-and-ordering). Registry position/state can still race between activations; the registry guard is separate work (SEK-G103). |
| `TagStateGrain`, streams/event delivery | Internal cache/replay correctness is self-healing through ETag grain storage and event-ID de-duplication. At-least-once delivery still requires idempotency for arbitrary consumer side effects. |

## Testing Without Orleans

Use `InMemorySekibanExecutor` (`src/Sekiban.Dcb/InMemory/InMemorySekibanExecutor.cs`) to run commands locally without a
silo. This executor spins in-process actors and stores events in memory—perfect for unit tests.

## Optional executor size gate on Orleans

Register `AddSekibanDcbExecutorSizeGate` before resolving `OrleansDcbExecutor` when the application wants a strict
pre-persistence budget. The Orleans publisher captures the resolver's destination plan and service identity once and
reuses that plan for enqueue, so a strict destination policy is valid only when that capability is available. A
non-strict unavailable or identity-mismatched destination policy writes an explicit diagnostic. This gate measures
executor-originated logical events or declared provider capabilities; it does not claim Azure Queue wire-envelope,
batch, or retry limits and does not change Orleans retry behavior.

## DCB Orleans version authority and whole-cluster upgrades

The DCB product line is aligned on `Microsoft.Orleans.*` **10.3.1**. Direct Orleans references in DCB source,
internal hosts, tests, and generated DCB templates are governed by the single DCB authority (or the template's
self-contained authority); keep every host on one consistent stable Orleans 10.x version. `Microsoft.Orleans.*`
10.3.1 publishes `net8.0` and `net10.0` asset groups only: a `net9.0` host resolves the `net8.0` group, whose
applicable `Microsoft.Extensions.*` floors are 8.0.x (for example, `Microsoft.Extensions.Hosting` 8.0.1), while a
`net10.0` host resolves the `net10.0` group, whose floor is 10.0.5. `Polly` 8.6.4 and `Polly.Extensions` 8.6.5 are
transitive in both groups.

Upgrade the whole Orleans cluster together. A rolling cluster which mixes 10.0.1 and 10.3.1 is not a supported or
verified compatibility guarantee. This dependency alignment changes runtime/package inputs only: it does not perform
a data or schema migration, does not make Azure Queue rewindable, and does not implement the separate #1185
subscriber-recovery work. Pure and Samples remain on their own supported dependency line.
