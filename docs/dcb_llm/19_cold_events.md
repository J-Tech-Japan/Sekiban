# Cold Events and Catch-up

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
> - [Cold Events and Catch-up](19_cold_events.md) (You are here)
> - [Materialized View Basics](20_materialized_view.md)

This document describes the cold event implementation currently used by the internal Orleans samples. It covers what gets written to cold storage, how export runs are coordinated, how hybrid read works, and how the dedicated catch-up worker is configured.

## Purpose

Cold events are used to move older, stable events out of the hot event store into cheaper object storage while keeping read consistency for catch-up and replay scenarios.

The current internal-use design has two goals:

- keep the write path on the hot store only
- let readers continue from cold segments plus recent hot events without changing application-level query code

## Main Components

Core packages and classes:

- `Sekiban.Dcb.Core/ColdEvents`
- `Sekiban.Dcb.ColdStorage/ColdEvents`
- `ColdExporter`
- `ColdExportBackgroundService`
- `HybridEventStore`
- `StorageBackedColdLeaseManager`
- `ColdCatalogReader`
- `AddSekibanDcbColdExport(...)`

Internal-use sample applications:

- `dcb/internalUsages/DcbOrleans.WithoutResult.ApiService`
- `dcb/internalUsages/DcbOrleans.Catchup.Functions`
- `dcb/internalUsages/DcbOrleans.AppHost`

## Storage Layout

Cold event data is organized by `serviceId`.

Control files:

- `control/{serviceId}/manifest.json`
- `control/{serviceId}/checkpoint.json`
- `control/{leaseId}/lease.json`

Segment files:

- `segments/{serviceId}/{fromSortableUniqueId}_{toSortableUniqueId}.jsonl`

Behavior:

- `manifest.json` is the catalog of exported segments and the latest safe exported boundary
- `checkpoint.json` stores the next hot-store cursor used by incremental export
- `lease.json` prevents multiple exporters from writing the same service concurrently
- segment files are JSONL and contain serialized `SerializableEvent` records

## Export Flow

The current exporter is incremental.

1. Acquire a storage-backed lease named `cold-export-{serviceId}`.
2. Load the checkpoint and read hot events after that boundary.
3. Apply the safe window so only sufficiently old events are exported.
4. Append to the last segment when possible, otherwise create new segment files.
5. Update manifest and checkpoint using optimistic concurrency via ETags.
6. Release the lease. If release fails, the exporter tries to expire the lease immediately.

Important operational details:

- lease duration is capped at 2 minutes even if the pull interval is longer
- manifest update retries up to 3 times on conflicts
- if no safe events exist yet, the export cycle exits without writing segments

## Hybrid Read Behavior

`AddSekibanDcbColdEventHybridRead()` replaces the registered `IEventStore` with `HybridEventStore`.

Read behavior:

- if cold events are disabled, reads go directly to the hot store
- if no manifest exists, reads go directly to the hot store
- if `since` is newer than the latest safe cold boundary, reads go directly to the hot store
- otherwise the reader loads matching cold segments first, then appends newer hot events, removes duplicates by event id, and sorts by `SortableUniqueId`

This is the key mechanism that allows catch-up readers to continue across archived ranges.

## Configuration

Cold export uses two related configuration sections.

`Sekiban:ColdEvent` controls feature behavior:

```json
{
  "Sekiban": {
    "ColdEvent": {
      "Enabled": true,
      "PullInterval": "00:30:00",
      "ExportCycleBudget": "00:03:00",
      "RunOnStartup": true,
      "SafeWindow": "00:02:00",
      "SegmentMaxEvents": 30000,
      "ExportMaxEventsPerRun": 30000,
      "SegmentMaxBytes": 536870912,
      "Storage": {
        "Provider": "azureblob",
        "Format": "jsonl",
        "AzureBlobClientName": "MultiProjectionOffload",
        "AzureContainerName": "multiprojection-cold-events"
      }
    }
  }
}
```

Legacy compatibility keys are still supported by `AddSekibanDcbColdExport(...)`:

```json
{
  "ColdExport": {
    "Interval": "00:05:00",
    "CycleBudget": "00:03:00"
  }
}
```

Storage options:

- `Provider`: `local` or `azureblob`
- `Format`: `jsonl`, `sqlite`, or `duckdb`
- `BasePath`: root directory for local storage, default `.cold-events`
- `Type`: legacy combined selector such as `jsonl`, `sqlite`, `duckdb`, `azureblob`

## Internal-use Wiring

### API service

The current internal API sample wires cold events explicitly:

```csharp
builder.Services.AddSekibanDcbColdEventDefaults();

if (builder.Configuration.GetSection("Sekiban:ColdEvent").GetValue<bool>("Enabled"))
{
    var coldConfig = builder.Configuration.GetSection("Sekiban:ColdEvent");
    var storageOptions = coldConfig.GetSection("Storage").Get<ColdStorageOptions>() ?? new ColdStorageOptions();
    var storageRoot = ColdObjectStorageFactory.ResolveStorageRoot(storageOptions, Directory.GetCurrentDirectory());

    builder.Services.AddSingleton(storageOptions);
    builder.Services.AddSingleton<IColdObjectStorage>(sp =>
        ColdObjectStorageFactory.Create(storageOptions, storageRoot, sp));
    builder.Services.AddSingleton<IColdLeaseManager, StorageBackedColdLeaseManager>();
    builder.Services.AddSekibanDcbColdEvents(options => coldConfig.Bind(options));
    builder.Services.AddSekibanDcbColdEventHybridRead();
}
```

This gives the API service:

- background cold export
- manual export endpoints
- progress and catalog endpoints
- hybrid read over cold and hot storage

### Catch-up worker

The dedicated worker uses the newer shared registration:

```csharp
builder.Services.AddSekibanDcbPostgresWithAspire();
builder.Services.AddSekibanDcbColdExport(
    builder.Configuration,
    builder.Environment.ContentRootPath);
```

This worker exists so export/catch-up responsibilities can run separately from the main API process with minimal setup.

### AppHost example

The internal Aspire AppHost currently sets values such as:

- `Sekiban:ColdEvent:Enabled=true`
- `Sekiban:ColdEvent:Storage:Provider=azureblob`
- `Sekiban:ColdEvent:Storage:Format=jsonl`
- `Sekiban:ColdEvent:Storage:AzureBlobClientName=MultiProjectionOffload`
- `Sekiban:ColdEvent:Storage:AzureContainerName=multiprojection-cold-events`
- `ColdExport:Interval=00:05:00`
- `ColdExport:CycleBudget=00:03:00`

## Internal API Endpoints

The internal API service exposes the following cold-event endpoints:

- `GET /api/cold/status`
- `GET /api/cold/progress`
- `GET /api/cold/catalog`
- `POST /api/cold/export`
- `POST /api/cold/export-now`

These are currently intended for internal diagnostics and operational tooling rather than as a public stable API surface.

## Supported Storage Backends

Current cold storage implementations:

- local JSONL
- local SQLite
- local DuckDB
- Azure Blob backed JSONL
- Azure Blob backed SQLite
- Azure Blob backed DuckDB

The selection logic is centralized in `ColdObjectStorageFactory`.

## Operational Notes

- Use a stable `serviceId`; cold data, checkpoint, and manifest are partitioned by it.
- Keep the safe window large enough to avoid exporting events that are still subject to reordering or ongoing writes.
- When using Azure Blob, the connection string named by `AzureBlobClientName` must exist.
- If the lease file becomes stale, the exporter can recover because lease expiration is time-based.
- Hybrid read depends on a valid manifest. If manifest or segment parsing fails, the implementation falls back to the hot store where possible.

## Recommended References

- [Storage Providers](11_storage_providers.md)
- [Orleans Setup](10_orleans_setup.md)
- [API Implementation](08_api_implementation.md)
- [Materialized View Basics](20_materialized_view.md)
