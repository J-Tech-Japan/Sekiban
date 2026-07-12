# Storage Providers - Azure and AWS Support

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
> - [Storage Providers](11_storage_providers.md) (You are here)
> - [Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)
> - [Cold Events and Catch-up](19_cold_events.md)
> - [Materialized View Basics](20_materialized_view.md)

DCB supports multiple cloud platforms for event persistence and projection snapshots. This guide covers configuration for both Azure and AWS.

## Platform Comparison

| Component | Azure | AWS |
|-----------|-------|-----|
| Event Store | Postgres / Cosmos DB | DynamoDB |
| Snapshot Storage | Azure Blob Storage | Amazon S3 |
| Orleans Clustering | Azure Table / Cosmos DB | RDS PostgreSQL |
| Orleans Streams | Azure Queue | Amazon SQS |

---

## Azure: Postgres Event Store

Package: `Sekiban.Dcb.Postgres` (`src/Sekiban.Dcb.Postgres`). Key tables:

- `dcb_events` – event payload, metadata, tags (JSONB)
- `dcb_tags` – tag → event linkage for tag-sliced queries

```csharp
builder.Services.AddSekibanDcbPostgres(configuration);
// or specify connection string directly
builder.Services.AddSekibanDcbPostgres("Host=localhost;Database=sekiban_dcb;Username=postgres;Password=postgres");
```

Run migrations with `Sekiban.Dcb.Postgres.MigrationHost` or let Aspire run the initializer in development.

## Azure: Cosmos DB Event Store

Package: `Sekiban.Dcb.CosmosDb` (`src/Sekiban.Dcb.CosmosDb`). Containers:

- `events` – partitioned by `/pk`
- `tags` – partitioned by `/pk`
- `multiProjectionStates` – partitioned by `/pk`

```csharp
services.AddSekibanDcbCosmosDbWithAspire();
// falls back to ConnectionStrings:SekibanDcbCosmos if Aspire client not found
```

## Azure: Blob Storage Snapshots

Package: `Sekiban.Dcb.BlobStorage.AzureStorage` (`src/Sekiban.Dcb.BlobStorage.AzureStorage`)

```csharp
services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
    new AzureBlobStorageSnapshotAccessor(
        sp.GetRequiredKeyedService<BlobServiceClient>("MultiProjectionOffload"),
        "multiprojection-snapshots"));
```

---

## AWS: DynamoDB Event Store

Package: `Sekiban.Dcb.DynamoDB` (`src/Sekiban.Dcb.DynamoDB`)

DynamoDB stores events with auto-table creation. Tables are created automatically on first write:

- `{prefix}_events` – event payload with SortableUniqueId as sort key
- Tag indexing via GSI for efficient tag queries

```csharp
builder.Services.AddSekibanDcbDynamoDb(options =>
{
    options.Region = "us-west-1";
    options.TablePrefix = "myapp";
});
```

### Configuration

```json
{
  "Sekiban": {
    "Database": "dynamodb"
  },
  "AWS_REGION": "us-west-1",
  "DYNAMODB_TABLE_PREFIX": "myapp"
}
```

## AWS: S3 Snapshot Storage

Package: `Sekiban.Dcb.BlobStorage.S3` (`src/Sekiban.Dcb.BlobStorage.S3`)

```csharp
services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
    new S3BlobStorageSnapshotAccessor(
        sp.GetRequiredService<IAmazonS3>(),
        "multiprojection-snapshots-bucket"));
```

---

## Configuration Tips

- Set `Sekiban:Database` to `postgres`, `cosmos`, or `dynamodb` to select the backend
- For Aspire development, use memory-based storage for rapid iteration
- Use managed identities (Azure) or IAM roles (AWS) for production credentials

## Operational Considerations

### Azure
- Monitor index usage on `dcb_tags` (Postgres) or RU consumption on Cosmos `tags` container
- Rotate secrets using Azure Key Vault

### AWS
- Monitor DynamoDB read/write capacity and throttling
- Use AWS Secrets Manager for RDS credentials
- S3 lifecycle policies can manage snapshot retention

## Storage Provider Summary

| Provider | Package | Offload Package | Status |
|----------|---------|-----------------|--------|
| Postgres | `Sekiban.Dcb.Postgres` | N/A (no limit) | Production |
| Cosmos DB | `Sekiban.Dcb.CosmosDb` | `Sekiban.Dcb.BlobStorage.AzureStorage` | Production |
| DynamoDB | `Sekiban.Dcb.DynamoDB` | `Sekiban.Dcb.BlobStorage.S3` | Production |
| SQLite | `Sekiban.Dcb.Sqlite` | N/A | Development |

## Consistency Contract

This section documents the actual atomicity guarantees of `IEventStore.WriteSerializableEventsAsync` / `WriteEventsAsync` per provider. The two-phase Cosmos write design itself is unchanged; what has changed is how a failure of that write is handled.

### Postgres — current guarantee

`PostgresEventStore.WriteEventsAsync` writes event rows and tag rows inside a single database transaction. Both **event-set atomicity** (all events in a `WriteEventsAsync` call commit, or none do) and **event/tag atomicity** (an event is never visible without its tag rows) are guaranteed.

### Cosmos DB — current guarantee

`CosmosDbEventStore.WriteSerializableEventsAsync` performs a two-phase write with **no transaction spanning the two phases**:

1. Event documents are created in parallel (`CreateItemAsync`), one per event, partitioned by `{serviceId}|{eventId}`.
2. Tag rows are then written via per-tag-partition `TransactionalBatch` (`{serviceId}|{tag}`).

This has two consequences today:

- **Event/tag crash window**: a crash or host termination between phase 1 and phase 2 (or mid-way through the phase 2 tag batches) leaves durable events without their tag rows. Those events are visible via `ReadAllEventsAsync`, but invisible to `ReadEventsByTagAsync`, tag projectors, and `GetLatestTagAsync` — which also feeds `GeneralTagConsistentActor`'s optimistic-concurrency baseline. A crash is not an in-process failure, so no policy below can prevent this window; only a repair pass can close it.
- **Multi-event partial visibility**: a multi-event `WriteEventsAsync` call can fail partway through its parallel event creates. Successfully created events become immediately visible to all-events readers even though sibling events from the same call may never get written. This is now reported as a `CosmosPartialEventWriteException` naming both the visible and the failed event ids, and **nothing is deleted** in response.

Tag rows are **derived deterministically** from the (event, tag) pair — `pk = {serviceId}|{tag}`, `id = {eventId}`, and the remaining fields from the event document, which carries the complete `tags` array (see `Models/CosmosEvent.cs` and `Tags/CosmosTagIdentity.cs`). Two things follow. Re-executing a tag write is safe: it re-derives identical rows, accepts the ones already present, and fills in the ones a partial write missed. And the `tags` container is a **derivable index** that can always be rebuilt from the `events` container. An existing tag row whose content disagrees with the event raises `CosmosTagIndexCorruptionException` and is never overwritten; that error is **not retryable** — the same content is derived on every attempt.

#### Write failure policy

`CosmosDbEventStoreOptions.WriteFailurePolicy` selects what happens when the tag-write phase fails in-process:

| Policy | Behavior |
|---|---|
| `Compatible` (**default**) | The behavior of earlier releases: the tag write is not retried, and if the (now `[Obsolete]`) `TryRollbackOnFailure` is set — it defaults to `true` — the already-written event documents are best-effort **deleted**. |
| `RollForward` (opt-in) | The tag write is **retried** (exponential backoff with jitter, an overall deadline, Cosmos `Retry-After` honored on 429, `CancellationToken` observed), converging on whatever rows a partial write left missing. Events are **never deleted**. If the retries run out, `CosmosTagWriteExhaustedException` names the events whose tag rows may be missing, and those events stay durable for a later repair. |

A server-sent `Retry-After` is honored in full: `MaxBackoff` caps the client's own backoff curve, not the server's instruction, so a `Retry-After` longer than `MaxBackoff` is **not** shortened — retrying before the server is ready would only earn another 429. `MaxTotalDuration` still bounds the whole sequence: if honoring the hint would cross the deadline, the write stops with `CosmosTagWriteExhaustedException` rather than retrying early.

**Rollback deletes durable events that all-events consumers — multi-projections above all — may already have read**, which contaminates their state irreversibly, and it only runs on an in-process exception, so it never runs after a crash. `RollForward` is the recommended setting for new deployments:

```csharp
services.AddSekibanDcbCosmosDb(
    configuration,
    options => options.WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward);
```

The default stays `Compatible` through the current release line so that **upgrading the package alone changes nothing** for an existing deployment. It flips to `RollForward` only at a major version boundary, with a documented migration.

#### Telemetry

The `Sekiban.Dcb.CosmosDb` meter (`CosmosDbTelemetry.MeterName`) publishes `tag_write.failures` (label `reason`: `transient` | `corruption`), `tag_write.retries`, `tag_write.retry_outcomes` (label `outcome`: `recovered` | `exhausted`), and `event_write.partial_failures`. Metric labels are drawn from small fixed sets; raw event ids and tag strings are unbounded and therefore appear only in the structured logs and in the exceptions above.

**Still planned (not yet released)**: a tag-index repair API and an opt-in startup sweep. Upgrading the package does not enable any repair or sweep behavior; those land later as explicit opt-ins. Note that reads immediately after a future repair may observe stale state depending on the configured Cosmos consistency level.

### Choosing a provider

Recommend **Postgres** for any workload that requires atomic event/tag visibility or event-set atomicity — for example money-sensitive workflows. Concrete examples in the Sekiban ecosystem:

- [SekibanWasmRuntime](https://github.com/J-Tech-Japan/SekibanWasmRuntime) defaults to Postgres for its event store, with Cosmos as an opt-in.
- SekibanAsAService uses separate management and runtime container lineages; each can select its provider independently, so apply the same Postgres-for-atomicity guidance per container.

## Materialized View Storage

Materialized views are currently implemented for PostgreSQL:

- `Sekiban.Dcb.MaterializedView` – core contracts and hosted catch-up worker
- `Sekiban.Dcb.MaterializedView.Postgres` – registry, executor, row access, and table updates
- `Sekiban.Dcb.MaterializedView.Orleans` – grain orchestration and query accessor

This is separate from the main event store package. In the current PoC, a service can store events in one database and
materialized view tables in another PostgreSQL database or schema. See [Materialized View Basics](20_materialized_view.md).

## Related

For the current internal-use cold event export, hybrid read, and catch-up worker setup, see [Cold Events and Catch-up](19_cold_events.md).
