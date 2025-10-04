# ResultBox - Functional Flow Control

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
> - [Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md) (You are here)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)

`ResultBox` (NuGet: ResultBoxes) is the glue that powers command handlers in both Sekiban Pure and DCB. It provides a
fluent, functional style for composing async validations, state lookups, and event creation.

## Core Concepts

- **ResultBox.Start** – entry point for building a pipeline.
- **Remap** – map current value to a new value.
- **Combine** – run an async step and carry both previous and new value forward.
- **Verify** – inject validation that can short-circuit with `ExceptionOrNone`.
- **Conveyor** – terminal step that returns `EventOrNone` (or any other payload).

See `internalUsages/Dcb.Domain/Enrollment/EnrollStudentInClassRoomHandler.cs` for a complete pipeline.

## Error Handling

- Use `ExceptionOrNone.FromException` to propagate business rule violations.
- The executor converts exceptions into `ResultBox.Error` so APIs can return meaningful messages.
- Combine multiple `Verify` steps to keep cross-entity invariants readable.

## Branching

You can branch by returning `TwoValues`, `ThreeValues`, etc., which keep multiple pieces of data flowing through the
pipeline. This is how multi-tag commands carry both student and classroom tags.

## Async Composition

`Combine` accepts tasks, making it easy to fetch tag state via `context.GetStateAsync`. Each step executes sequentially,
ensuring reservations only happen after validations succeed.

## Returning Multiple Events

Handlers can append additional events via `context.AppendEvent(...)`. `ResultBox` pipelines typically return the primary
event via `EventOrNone`, while the context collects appended ones. The executor deduplicates them before persistence.

## Testing Pipelines

`ResultBox` exposes `IsSuccess`, `GetValue`, and `GetException` so you can unit test handlers without the executor. Most
handlers are static methods returning `Task<ResultBox<EventOrNone>>`, enabling direct invocation in tests.

## Tips

- Keep each step small—favor additional `Remap`/`Combine` calls over large lambdas.
- Surface domain-specific exceptions (e.g., `MaxClassCountExceededException`) for richer API responses.
- Document pipelines with comments when they encode non-obvious business logic.
