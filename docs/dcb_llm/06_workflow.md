# Command Workflow - Reservations & Persistence

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Commands, Events, Tags, Projectors](03_aggregate_command_events.md)
> - [MultiProjection](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Command Workflow](06_workflow.md) (You are here)
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

DCB’s executor coordinates validation, state access, tag reservations, persistence, and publication. Understanding this
flow helps you design commands that behave well under contention.

## High-Level Flow

1. **Validate Command** – Data annotations are evaluated (`CommandValidator`).
2. **Create Context** – `GeneralCommandContext` tracks accessed tag states and appended events.
3. **Execute Handler** – Your handler returns `EventOrNone` and optionally appends extra events.
4. **Collect Tags** – All tags across events are deduplicated.
5. **Reserve Consistency Tags** – TagConsistent actors validate optimistic versions and hold reservations.
6. **Persist Events** – A single `IEventStore.WriteEventsAsync` call stores events and tag links.
7. **Confirm Reservations** – Successful reservations are confirmed so actors know to re-catch-up.
8. **Publish** – Optional `IEventPublisher` forwards events to streams or external buses.
9. **Return Result** – `ExecutionResult` includes the `SortableUniqueId` and tag write outcomes.

Source: `src/Sekiban.Dcb/Actors/GeneralSekibanExecutor.cs`.

## Reservation Mechanics

- Tags that return `false` from `IsConsistencyTag()` are skipped.
- If the tag is a `ConsistencyTag` wrapper with a known `SortableUniqueId`, the executor reuses it for OCC.
- Otherwise, the executor looks up the tag’s last `SortableUniqueId` from previously fetched state.
- Reservations time out based on `TagConsistentActorOptions.CancellationWindowSeconds`
  (`src/Sekiban.Dcb/Actors/GeneralTagConsistentActor.cs`).

Conflicts return a `ResultBox` error; the executor cancels any held reservations before propagating the exception.

## Event Persistence

`WriteEventsAsync` returns both the serialized events and a list of `TagWriteResult` entries describing each tag written.
Backends must atomically persist the events and their tag rows:

- Postgres implementation: `src/Sekiban.Dcb.Postgres/PostgresEventStore.cs`
- Cosmos DB implementation: `src/Sekiban.Dcb.CosmosDb/CosmosDbEventStore.cs`

Both backends use the `SortableUniqueId` to guarantee ascending order and simplify catch-up queries.

## Observability

`ExecutionResult` captures:

- `CommandId` (`Guid`) – Identifier for the persisted event batch
- `ProducedEvents` – Count of events written (normally one)
- `TagWriteResults` – Reservation + write details per tag
- `Elapsed` – Total execution time
- `SortableUniqueIds` – The ids assigned to each persisted event

Add logging around command execution to surface reservation retries and persistence outcomes.

## Failure Modes

- **Validation Errors** – Thrown before any reservation; return HTTP 400 from APIs.
- **Reservation Conflict** – Another command mutated one of the consistency tags first; return 409 and retry upstream.
- **Persistence Failure** – The event store call failed; executor best-effort cancels reservations and returns 500.
- **Confirmation Failure** – Rare; reservation stays until expiry. Retrying the command usually succeeds once actors
  catch up.

## Retrying Commands

Commands should be idempotent. Store all random identifiers (e.g., PKs) inside the command payload so a retry produces
the same event.

## Extensibility Points

- **Custom Event Publisher** – Implement `IEventPublisher` to forward events to Azure Queue, Kafka, etc.
- **Custom Actor Host** – Implement `IActorObjectAccessor` to plug in alternative actor frameworks.
- **Command Middleware** – Wrap `ISekibanExecutor` with decorators for telemetry or policies.

For a visual walkthrough see `tasks/dcb.design/dcb.concept.md` (sequence diagram and state machine).
