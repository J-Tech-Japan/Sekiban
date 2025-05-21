# Core Concepts - Sekiban Event Sourcing

> **Navigation**
> - [Core Concepts](01_core_concepts.md) (You are here)
> - [Getting Started](02_getting_started.md)
> - [Aggregate, Projector, Command and Events](03_aggregate_command_events.md)
> - [Multiple Aggregate Projector](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Workflow](06_workflow.md)
> - [JSON and Orleans Serialization](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md)
> - [Client API (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md)
> - [Unit Testing](11_unit_testing.md)
> - [Common Issues and Solutions](12_common_issues.md)

## Core Concepts

Event Sourcing: Store all state changes as immutable events. Current state is derived by replaying events.

## Naming Conventions

- Commands: Imperative verbs (Create, Update, Delete)
- Events: Past tense verbs (Created, Updated, Deleted)
- Aggregates: Nouns representing domain entities
- Projectors: Named after the aggregate they project

## Key Principles of Event Sourcing

Event sourcing is an architectural pattern where:

1. **State Changes as Events**: All changes to application state are stored as a sequence of events
2. **Immutable Event Log**: Once recorded, events are never modified or deleted
3. **Current State via Projection**: The current state is calculated by replaying events in sequence
4. **Complete Audit Trail**: The event log provides a complete history of all changes

## Benefits of Using Sekiban

1. **Full History**: Complete audit trail of all domain changes
2. **Time Travel**: Ability to reconstruct state at any point in time
3. **Domain Focus**: Better separation of concerns with clear domain models
4. **Scalability**: Can scale read and write operations independently
5. **Event-Driven Architecture**: Natural integration with event-driven systems

## Core Components

- **Aggregate**: Domain entity that encapsulates state and business rules
- **Command**: Represents user intention to change system state
- **Event**: Immutable record of a state change that has occurred
- **Projector**: Builds current state by applying events to aggregates
- **Query**: Retrieves data from the system based on current state

## Event Sourcing vs. Traditional CRUD

| Aspect            | Event Sourcing                                  | Traditional CRUD                           |
|-------------------|------------------------------------------------|-------------------------------------------|
| Data Storage      | Immutable event log                            | Mutable state records                      |
| State Management  | Derived from event sequence                    | Direct manipulation of current state       |
| History           | Complete history preserved                      | Limited history or requires separate logs  |
| Concurrency       | Natural conflict resolution via event sequence | Requires locking or optimistic concurrency |
| Audit Trail       | Built-in                                       | Requires additional implementation         |
| Temporal Queries  | Native support for historical state            | Difficult, requires additional design      |
| Domain Modeling   | Encourages behavior-rich domain models         | Often leads to anemic domain models        |

## Sekiban Architecture

Sekiban implements a clean, modern approach to event sourcing with:

1. **Orleans Integration**: Highly scalable, distributed runtime
2. **JSON Serialization**: Flexible and human-readable event storage
3. **Strong Typing**: Type-safe commands, events, and aggregates
4. **Minimal Infrastructure**: Simple setup with minimal configuration
5. **Source Generation**: Automatic registration of domain types