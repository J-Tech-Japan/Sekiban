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

- `events` – partitioned by `/id`
- `tags` – partitioned by `/tag`

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
