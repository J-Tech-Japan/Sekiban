# Core Concepts - Dynamic Consistency Boundary (DCB)

> **Navigation**
> - [Core Concepts](01_core_concepts.md) (You are here)
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
> - [Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)

## What is DCB?

Dynamic Consistency Boundary (DCB) is the next evolution of Sekiban’s event sourcing runtime. Instead of static
aggregate streams, every business operation is recorded as a single event in a globally ordered log and tagged with all
entities that participate in the transaction. Consistency becomes a runtime decision—commands declare the tags they
need to protect, and the executor coordinates reservations to ensure no conflicting command writes interleaving
mutations to the same tags.

Key ideas:

- **Single Business Fact = Single Event**: Capture an entire operation—no more splitting a transfer into separate debit
  and credit events.
- **Single Global Event Stream**: Events are timestamp-ordered via `SortableUniqueId` so every backend can replay the same
  timeline.
- **Tag-Based Boundaries**: Tags implement `ITag` and announce whether they are part of the consistency boundary via
  `IsConsistencyTag()`.
- **Optimistic Concurrency**: Tag reservations include the last observed `SortableUniqueId`. Updates fail fast if someone
  else already pushed a newer event for the same tag.

## Why DCB?

Traditional aggregate-centric event sourcing forces design-time consistency scopes. Cross-aggregate flows require sagas,
intermediate queues, and eventual reconciliation. DCB removes that friction:

- **Dynamic Scope**: Each command specifies the tags it touches—perfect for workflows that span multiple domain actors.
- **Stronger Consistency**: A single `IEventStore.WriteEventsAsync` call persists the business fact and every tag link.
- **Actor Friendly**: Orleans (and future actor hosts) map one-to-one with tags, providing isolation, caching, and
  lifecycle management.
- **Better Auditability**: Because every command produces exactly one event, downstream consumers see the same fact the
  domain modeled.

## DCB vs. Aggregate Event Sourcing

| Aspect | Aggregate-Based Event Sourcing | Dynamic Consistency Boundary |
| --- | --- | --- |
| Streams | Per aggregate | Single global stream |
| Consistency | Static per aggregate | Per-command dynamic tag set |
| Cross-entity transactions | Sagas / eventual consistency | Immediate consistency within reserved tags |
| Concurrency control | Aggregate version | Multi-tag optimistic concurrency using `SortableUniqueId` |
| Event shape | Multiple domain-specific events | One business fact per command |

## Core Runtime Components

- **Events (`IEventPayload`)**: Immutable business facts serialized via the registered domain types. Records listed in
  `tasks/dcb.design/records.md` describe the wire format.
- **Tags (`ITag`)**: Identify every entity impacted by an event. String representation `"[Group]:[Content]"` lets stores
  index and query efficiently. See `internalUsages/Dcb.Domain/Student/StudentTag.cs` for a typical implementation.
- **GeneralSekibanExecutor**: Coordinates command validation, tag state materialization, reservations, event persistence,
  and publication (`src/Sekiban.Dcb/Actors/GeneralSekibanExecutor.cs`).
- **TagStateActor / TagConsistentActor**: Actor implementations maintain cached projections and reservation state
  (`src/Sekiban.Dcb/Actors/GeneralTagStateActor.cs`, `src/Sekiban.Dcb/Actors/GeneralTagConsistentActor.cs`). Orleans
  grains wrap these actors for distributed execution (`src/Sekiban.Dcb.Orleans/Grains/TagStateGrain.cs`).
- **Event Store**: Provides ordered persistence and tag lookup. Postgres (`src/Sekiban.Dcb.Postgres/PostgresEventStore.cs`)
  and Cosmos DB (`src/Sekiban.Dcb.CosmosDb/CosmosDbEventStore.cs`) share the same contract.

## Benefits

1. **Flexible Consistency Boundaries**: Choose just the tags required to enforce invariants.
2. **Simpler Cross-Entity Workflows**: Model real business operations without choreography layers.
3. **Scalability Through Actors**: Tags become grains: hot tags are isolated, cold tags consume zero resources.
4. **Rich Query Capabilities**: MultiProjection and tag state projections deliver fast read models without coupling to the
   write path.
5. **Observability**: `EventMetadata` carries correlation/causation identifiers so you can trace a command through the
   runtime.

## Design Principles

- **Consistency is Opt-in**: Tags that return `false` from `IsConsistencyTag()` participate in projections but do not
  take part in reservation.
- **Retries Over Compensation**: Commands are expected to be idempotent; the executor cancels reservations on failure but
  does not attempt side-effect compensation.
- **Deterministic Projections**: Projectors are pure static methods—no I/O, no hidden state. Orleans caches and replay
  them deterministically.
- **Storage-Agnostic**: As long as the backend can guarantee ordered writes and conditional appends, it can implement the
  `IEventStore` contract.

## Related Reading

- Conceptual deep dive: `tasks/dcb.design/dcb.concept.md`
- Interface overview: `tasks/dcb.design/interfaces.md`
- Record shape reference: `tasks/dcb.design/records.md`
- Public event stream narrative: <https://dcb.events>
