# Client UI (Blazor) - Consuming DCB APIs

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Commands, Events, Tags, Projectors](03_aggregate_command_events.md)
> - [MultiProjection](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Command Workflow](06_workflow.md)
> - [Serialization & Domain Types](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md)
> - [Client UI (Blazor)](09_client_api_blazor.md) (You are here)
> - [Orleans Setup](10_orleans_setup.md)
> - [Storage Providers](11_storage_providers.md)
> - [Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)

The template includes a Blazor Server app (`internalUsages/DcbOrleans.Web`) that demonstrates how to call DCB APIs and
render projection data in real time.

## API Client

`StudentApiClient` wraps `HttpClient` and exposes strongly typed methods for commands and queries.

```csharp
public async Task<StudentState[]> GetStudentsAsync(int? pageNumber = null, int? pageSize = null, string? waitForSortableUniqueId = null)
{
    var query = QueryString();
    return await httpClient.GetFromJsonAsync<List<StudentState>>(query) ?? [];
}

public async Task<CommandResponse> CreateStudentAsync(CreateStudent command)
{
    var response = await httpClient.PostAsJsonAsync("/api/students", command);
    // Return event id + sortable unique id to drive wait-for queries
}
// Source: internalUsages/DcbOrleans.Web/StudentApiClient.cs
```

`CommandResponse` carries the command outcome plus the `SortableUniqueId` so the UI can immediately re-query waiting for
fresh data.

## UI Patterns

- **Modal Forms** – Students page uses `EditForm` with data annotations for client-side validation
  (`internalUsages/DcbOrleans.Web/Components/Pages/Students.razor`).
- **Pagination** – Query parameters (`pageNumber`, `pageSize`) map directly to MultiProjection list queries.
- **Wait-For Refresh** – After a successful command, the UI calls `LoadStudents(result.SortableUniqueId)` so the API waits
  until the projection processes the new event.
- **StreamRendering** – Components enable interactive server rendering for low-latency updates.

## Shared Layout

`MainLayout.razor` and `NavMenu.razor` provide navigation between Students, Classrooms, Enrollments, and Weather pages.
Each page follows the same pattern: call a typed API client, display projection data, and surface command results.

## Dependency Injection

`Program.cs` registers typed clients using `IHttpClientFactory` and wires them for server-side rendering (`builder.Services.AddHttpClient<StudentApiClient>(...)`).

## Error Handling

- Display API errors inside modals using the `Error` field on the view model.
- Log exceptions with injected `ILogger<T>` to capture serialization or connectivity issues.
- Consider global toast/notification services for richer UX.

## Extending the UI

1. Add a new query/command to the domain and register it with `DcbDomainTypes`.
2. Expose endpoints in the API project.
3. Generate a typed API client method.
4. Build a Blazor component that calls the client and renders the result.

The same pattern works for other front-end stacks—Blazor simply highlights how to deal with `waitForSortableUniqueId` and
ResultBox outcomes from the API.
