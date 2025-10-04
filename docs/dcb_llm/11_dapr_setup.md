# Storage Providers - Postgres, Cosmos, Azure Storage

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
> - [Storage Providers](11_dapr_setup.md) (You are here)
> - [Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)

DCB currently targets Postgres and Cosmos DB for event persistence, plus Azure Blob Storage for MultiProjection
snapshots. Dapr integration is on the roadmap; use Orleans for actor hosting today.

## Postgres Event Store

Package: `Sekiban.Dcb.Postgres` (`src/Sekiban.Dcb.Postgres`). Key tables:

- `dcb_events` – event payload, metadata, tags (JSONB)
- `dcb_tags` – tag → event linkage for tag-sliced queries

Install via DI:

```csharp
builder.Services.AddSekibanDcbPostgres(configuration);
// or specify connection string directly
builder.Services.AddSekibanDcbPostgres("Host=localhost;Database=sekiban_dcb;Username=postgres;Password=postgres");
```

Run migrations with `Sekiban.Dcb.Postgres.MigrationHost` or let Aspire run the initializer in development.

## Cosmos DB Event Store

Package: `Sekiban.Dcb.CosmosDb` (`src/Sekiban.Dcb.CosmosDb`). Containers:

- `events` – partitioned by `/id`
- `tags` – partitioned by `/tag`

Register with configuration or Aspire integration:

```csharp
services.AddSekibanDcbCosmosDbWithAspire();
// falls back to ConnectionStrings:SekibanDcbCosmos if Aspire client not found
```

Cosmos writes are best-effort transactional; the executor still guarantees tag reservations but durability depends on the
Cosmos account configuration.

## Snapshot Storage

Large MultiProjections can offload snapshots to Azure Blob Storage using `Sekiban.Dcb.BlobStorage.AzureStorage`
(`src/Sekiban.Dcb.BlobStorage.AzureStorage`). Register an accessor:

```csharp
services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
    new AzureBlobStorageSnapshotAccessor(
        sp.GetRequiredKeyedService<BlobServiceClient>("MultiProjectionOffload"),
        "multiprojection-snapshots"));
```

## Configuration Tips

- Set `Sekiban:Database` to `postgres` or `cosmos` to select the backend.
- For Aspire, add keyed Azure resources (Tables, Queues, Blobs) so Orleans and the event store share infrastructure.
- Use connection strings or managed identities depending on your deployment environment.

## Operational Considerations

- Monitor index usage on `dcb_tags` (Postgres) or RU consumption on Cosmos `tags` container.
- Rotate secrets using Azure Key Vault or environment variables; the template reads from both.
- Run migrations before deploying new domains that introduce additional columns/indices.

## Planned Integrations

Dapr-based actor hosting is not yet available for DCB. Once ready, expect adapters implementing `IActorObjectAccessor`
and `IEventStore` that leverage Dapr state stores and pub/sub.
