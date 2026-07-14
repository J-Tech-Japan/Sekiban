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
> - [Storage Providers](11_storage_providers.md)
> - [Testing](12_unit_testing.md) (You are here)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)

DCB ships with an in-memory executor and event store that make unit testing straightforward. You can exercise command
handlers, reservation logic, and projectors without spinning up Orleans or a real database.

## In-Memory Harness

The in-memory harness lives in the **Testing packages** — reference them from your test projects, and from nothing
else:

| package | what you get |
|---|---|
| `Sekiban.Dcb.Core.Testing` | `InMemoryEventStore`, `InMemoryObjectAccessor`, `InMemoryMultiProjectionStateStore`, … |
| `Sekiban.Dcb.WithResult.Testing` | `InMemoryDcbExecutorForTesting` (ResultBox facade) |
| `Sekiban.Dcb.WithoutResult.Testing` | `InMemoryDcbExecutorForTesting` (exception-based facade) |

```csharp
using Sekiban.Dcb.Testing;

var domainTypes = DomainType.GetDomainTypes();

// Pass the event types your domain registered. The parameterless overload discovers a DIFFERENT set by reflection,
// which is a quiet way to make a test pass against a store that is not serializing what your domain serializes.
var eventStore = new InMemoryEventStore(domainTypes.EventTypes);

var executor = new InMemoryDcbExecutorForTesting(domainTypes, eventStore);
```

Or compose it by hand, when a test wants the pieces:

```csharp
var accessor = new InMemoryObjectAccessor(eventStore, domainTypes);
var executor = new GeneralSekibanExecutor(eventStore, accessor, domainTypes);
```

The old types in `Sekiban.Dcb.InMemory` still work identically and are `[Obsolete]`, not removed. They are deprecated
because a production system once composed itself out of them — see
[Localhost Orleans](22_localhost_orleans.md) for the taxonomy (real / local / test) and the migration table.

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
