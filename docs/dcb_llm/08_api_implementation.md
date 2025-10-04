# API Implementation - Minimal APIs for DCB

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Commands, Events, Tags, Projectors](03_aggregate_command_events.md)
> - [MultiProjection](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Command Workflow](06_workflow.md)
> - [Serialization & Domain Types](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md) (You are here)
> - [Client UI (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md)
> - [Storage Providers](11_dapr_setup.md)
> - [Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)

The sample API (`internalUsages/DcbOrleans.ApiService/Program.cs`) uses .NET Minimal APIs. Each endpoint injects
`ISekibanExecutor`, executes commands or queries, and translates `ResultBox` outcomes into HTTP responses.

## Structure

```csharp
var apiRoute = app.MapGroup("/api");

apiRoute.MapPost("/students", async (CreateStudent command, ISekibanExecutor executor) =>
{
    var result = await executor.ExecuteAsync(command);
    return result.IsSuccess
        ? Results.Ok(new
        {
            command.StudentId,
            eventId = result.GetValue().EventId,
            sortableUniqueId = result.GetValue().SortableUniqueId
        })
        : Results.BadRequest(new { error = result.GetException().Message });
});
```

Commands that need custom handlers call the overload with `handlerFunc`, e.g. classroom creation uses
`CreateClassRoomHandler.HandleAsync` (`internalUsages/Dcb.Domain/ClassRoom/CreateClassRoomHandler.cs`).

## Query Endpoints

List endpoints accept pagination plus optional `waitForSortableUniqueId` that flows into the query record. The executor
waits until the MultiProjection grain confirms the sortable id has been processed.

```csharp
apiRoute.MapGet("/students", async (ISekibanExecutor executor, int? pageNumber, int? pageSize, string? waitFor) =>
{
    var query = new GetStudentListQuery
    {
        PageNumber = pageNumber ?? 1,
        PageSize = pageSize ?? 20,
        WaitForSortableUniqueId = waitFor
    };
    var result = await executor.QueryAsync(query);
    return result.IsSuccess
        ? Results.Ok(result.GetValue().Items)
        : Results.BadRequest(new { error = result.GetException().Message });
});
```

For direct tag state reads the API constructs a `TagStateId` and calls `GetTagStateAsync`.

## Cross-Cutting Concerns

- **Problem Details & Validation** – Add `builder.Services.AddProblemDetails();` and surface validation errors from
  `CommandValidationException` as 400 responses.
- **CORS** – Enabled in the template for the Blazor front-end.
- **OpenAPI & Scalar** – Development builds expose interactive API docs (`app.MapOpenApi(); app.MapScalarApiReference();`).
- **Logging** – Filter noisy Azure SDK logs in development to keep the console readable.

## Error Mapping

Translate `ResultBox` failures to HTTP status codes:

- Validation → 400
- Reservation conflict → 409 (`InvalidOperationException` with “Failed to reserve tags”)
- Event store failure → 500

Wrap the executor in policy decorators (retry, circuit breaker) if your event store may experience transient errors.

## Authentication & Authorization

The template leaves auth empty. Because command handlers should not inspect caller identity, enforce authorization at the
API layer (e.g., `RequireAuthorization`). Propagate the user id into `EventMetadata` via a custom executor decorator so
business events retain auditing information.

## Streaming & Notification Hooks

Register `IEventPublisher` implementations (e.g., `OrleansEventPublisher` in `src/Sekiban.Dcb.Orleans/OrleansEventPublisher.cs`)
when you need to stream events to Orleans streams or external buses. The API often returns the `SortableUniqueId` so
clients can follow up with a `waitFor` query.
