# Testing - Verifying DCB Domains

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
> - [Storage Providers](11_dapr_setup.md)
> - [Testing](12_unit_testing.md) (You are here)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)

DCB ships with an in-memory executor and event store that make unit testing straightforward. You can exercise command
handlers, reservation logic, and projectors without spinning up Orleans or a real database.

## In-Memory Harness

- `InMemoryEventStore` – stores events and tags in memory (`src/Sekiban.Dcb/InMemory/InMemoryEventStore.cs`).
- `InMemoryObjectAccessor` – actor accessor that spins up in-memory TagState/TagConsistent actors on demand.
- `GeneralSekibanExecutor` – same executor used in production; inject the in-memory components.

```csharp
var eventStore = new InMemoryEventStore();
var domainTypes = DomainType.GetDomainTypes();
var accessor = new InMemoryObjectAccessor(eventStore, domainTypes);
var executor = new GeneralSekibanExecutor(eventStore, accessor, domainTypes);
```

## Testing Optimistic Concurrency

`tests/Sekiban.Dcb.Tests/OptimisticLockingTest.cs` demonstrates how to assert reservation behavior:

- Use `ConsistencyTag.FromTagWithSortableUniqueId` to embed a known version.
- Verify that mismatched versions yield a `Failed to reserve tags` error.
- Ensure retry without version picks up the latest sortable id.

## Projector Tests

Because projectors are pure static methods, test them directly by feeding events and asserting on the returned payloads.
You can instantiate `TagState` records manually or use `GeneralTagStateActor` in memory to replay events.

## Query Tests

- Seed `InMemoryEventStore` with events.
- Run commands through the executor to generate consistent tag state.
- Execute list/single queries via `executor.QueryAsync` against the in-memory MultiProjection (use
  `InMemoryMultiProjectionGrain` from `tests/Sekiban.Dcb.Orleans.Tests` for end-to-end scenarios).

## Integration Tests with Orleans

`tests/Sekiban.Dcb.Orleans.Tests` spins up a test silo using `TestClusterBuilder`. Use these when you need to verify stream
processing, snapshot offloading, or behavior that depends on Orleans timers.

## Assertions and Helpers

- `ResultBox` exposes `IsSuccess`, `GetValue()`, and `GetException()` for fluent assertions.
- `SortableUniqueId` contains helper methods to validate ordering and timestamps.
- Use factory methods in your domain (e.g., `DomainType.GetDomainTypes()`) to ensure test registrations match the real app.

## CI Considerations

- Run memory-only tests quickly in unit test pipelines.
- For integration tests, run Orleans + Postgres containers via Docker compose or Aspire orchestrations.
- Use deterministic GUIDs in test commands to keep event snapshots reproducible.
