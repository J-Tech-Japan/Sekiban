# Common Issues and Solutions - DCB

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
> - [Common Issues and Solutions](13_common_issues.md) (You are here)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)

## Failed to Reserve Tags

**Symptoms**: `InvalidOperationException` with message "Failed to reserve tags: ..."

**Causes**:
- Optimistic concurrency mismatch (someone else wrote newer events).
- Tag already reserved and not yet confirmed (long-running command, actor restart).

**Fixes**:
- Include `ConsistencyTag.FromTagWithSortableUniqueId` when retrying after a read to guarantee correct version.
- Inspect `TagConsistentActorOptions.CancellationWindowSeconds` to adjust hold timeout.
- Monitor reservations via custom telemetry around `MakeReservationAsync`.

## Missing Type Registration

**Symptoms**: `InvalidOperationException` about unknown event/tag/projector.

**Fixes**: Ensure the type is registered in `DomainType.GetDomainTypes()` (`internalUsages/Dcb.Domain/DomainType.cs`).
Remember to register query types when introducing new projections.

## Projection Not Updating

**Symptoms**: API returns stale data, `WaitForSortableUniqueId` times out.

**Causes**:
- MultiProjection grain fell behind due to stream disconnection.
- Projection version mismatch (bumped in code but not deployed).

**Fixes**:
- Check Orleans dashboard for grain exceptions.
- Restart the silo to force catch-up.
- Verify that projection version strings match what is registered in domain types.

## Postgres Errors on Startup

**Symptoms**: Migration failures or missing tables.

**Fixes**:
- Run `dotnet run --project Sekiban.Dcb.Postgres.MigrationHost` before starting the silo.
- Ensure connection string points to a database where the user has create/alter permissions.
- Confirm `Sekiban:Database` configuration is set to `postgres`.

## Cosmos Throughput Limits

**Symptoms**: `429` (Request rate too large) exceptions.

**Fixes**:
- Increase RU/s on the `events` and `tags` containers.
- Use `WaitForSortableUniqueId` sparingly in read-heavy scenarios to reduce polling frequency.

## Serialization Exceptions

**Symptoms**: `JsonException` during event replay or API responses.

**Fixes**:
- Keep event payloads backward compatible; avoid removing required properties.
- When renaming records, register both old and new names via custom converters.
- Validate that `[GenerateSerializer]` attributes exist for Orleans-managed payloads.

## Azure Queue Stream Issues

**Symptoms**: Missing events in projections when using Azure Queue streams.

**Fixes**:
- Ensure queues exist and the service principal has permissions.
- Adjust `BatchContainerBatchSize` and `GetQueueMsgsTimerPeriod` for throughput vs latency trade-offs.
- If running locally, verify Azurite connection strings.

## Dapr Integration

Not yet available. If you see references to `Sekiban.Pure.Dapr`, they apply to the pure aggregate runtime, not DCB.
Stick with Orleans until Dapr support ships.
