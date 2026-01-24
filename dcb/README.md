# Sekiban DCB - Dynamic Consistency Boundary

**Sekiban DCB** is the recommended event sourcing implementation for new projects. It uses tag-based consistency boundaries instead of traditional aggregates, enabling more flexible cross-entity transactions without saga complexity.

📚 **Documentation**: [sekiban.dev](https://www.sekiban.dev/)

## Sekiban Implementations

| Implementation | Description | Status |
|---------------|-------------|--------|
| **Sekiban DCB** | Dynamic Consistency Boundary - tag-based event sourcing | ✅ Recommended |
| Sekiban.Pure | Traditional aggregate-based event sourcing | ⚠️ Deprecated |

## Quick Start

```bash
dotnet new install Sekiban.Pure.Templates
dotnet new sekiban-orleans-aspire -n YourProjectName
```

## Key Features

- **Tag-based consistency**: Define consistency scope per command, not per aggregate
- **No saga complexity**: Cross-entity invariants without compensating events
- **Optimistic concurrency**: Tag-based conflict detection with `SortableUniqueId`
- **Actor model**: Microsoft Orleans integration for scalability
- **Multi-cloud**: Azure (Cosmos DB) and AWS (DynamoDB) support

## Supported Event Stores

| Event Store | Package | Cloud |
|-------------|---------|-------|
| Cosmos DB | `Sekiban.Dcb.CosmosDb` | Azure |
| PostgreSQL | `Sekiban.Dcb.Postgres` | Any |
| DynamoDB | `Sekiban.Dcb.DynamoDB` | AWS |
| SQLite | `Sekiban.Dcb.Sqlite` | Local/Dev |

## Snapshot Storage

| Storage | Package | Cloud |
|---------|---------|-------|
| Azure Blob | `Sekiban.Dcb.BlobStorage.AzureStorage` | Azure |
| Amazon S3 | `Sekiban.Dcb.BlobStorage.S3` | AWS |

## DCB Packages

| Package | Description |
|---------|-------------|
| `Sekiban.Dcb.Core` | Core framework |
| `Sekiban.Dcb.Core.Model` | Domain model interfaces |
| `Sekiban.Dcb.WithResult` | ResultBox integration |
| `Sekiban.Dcb.Orleans.WithResult` | Orleans + ResultBox |
| `Sekiban.Dcb.Postgres` | PostgreSQL event store |
| `Sekiban.Dcb.CosmosDb` | Cosmos DB event store |
| `Sekiban.Dcb.DynamoDB` | DynamoDB event store |
| `Sekiban.Dcb.Sqlite` | SQLite event store |
| `Sekiban.Dcb.BlobStorage.AzureStorage` | Azure Blob snapshots |
| `Sekiban.Dcb.BlobStorage.S3` | S3 snapshots |

## What is DCB?

Dynamic Consistency Boundary (DCB) replaces rigid per-aggregate transactional boundaries with context-sensitive consistency based on event tags. Each event carries tags representing affected entities, and consistency is enforced only on the tags reserved by a command.

Learn more at [dcb.events](https://dcb.events)

## Documentation

- **Website**: [sekiban.dev](https://www.sekiban.dev/)
- **DCB Docs**: [docs/dcb_llm](https://github.com/J-Tech-Japan/Sekiban/tree/main/docs/dcb_llm) (EN) | [docs/dcb_llm_ja](https://github.com/J-Tech-Japan/Sekiban/tree/main/docs/dcb_llm_ja) (JP)

## License

Apache 2.0 - Copyright (c) 2022- J-Tech Japan
