# Query - Reading from DCB

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Commands, Events, Tags, Projectors](03_aggregate_command_events.md)
> - [MultiProjection](04_multiple_aggregate_projector.md)
> - [Query](05_query.md) (You are here)
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

DCB queries read from MultiProjection grains or directly from tag state actors. The Orleans executor wraps these access
patterns behind `ISekibanExecutor.QueryAsync` overloads.

## Query Interfaces

- `IQueryCommon<TResult>` – Single-result projection
- `IListQueryCommon<TResult>` – Paginated list query
- `IWaitForSortableUniqueId` – Optional interface that forces the query to wait until a given event has been processed

Construct strong types that describe the request payload and static metadata required to locate the right projector.

```csharp
public record GetClassRoomListQuery(int Page = 1, int PageSize = 20)
    : IMultiProjectionListQuery<GenericTagMultiProjector<ClassRoomProjector, ClassRoomTag>,
        GetClassRoomListQuery, ClassRoomItem>
{
    public static string ProjectionName => GenericTagMultiProjector<ClassRoomProjector, ClassRoomTag>.MultiProjectorName;
    public static string ProjectionVersion => GenericTagMultiProjector<ClassRoomProjector, ClassRoomTag>.MultiProjectorVersion;

    public int GetPage() => Page;
    public int GetPageSize() => PageSize;
}
// Source: internalUsages/Dcb.Domain/Queries/GetClassRoomListQuery.cs
```

## Executing Queries

Inject `ISekibanExecutor` and call the appropriate overload:

```csharp
var result = await executor.QueryAsync(new GetStudentListQuery(page: 1, pageSize: 10));
if (result.IsSuccess)
{
    var list = result.GetValue();
    // list.Items contains projection payloads
}
```

List queries return `ListQueryResult<T>` (count, items, pagination metadata). Single queries return the projection type.

## Waiting for Fresh Data

When a command returns a `SortableUniqueId`, propagate it to the query via `IWaitForSortableUniqueId` so the executor can
wait until the projection catches up.

```csharp
public record GetWeatherForecastCountQuery(string? WaitForSortableUniqueId = null)
    : IMultiProjectionQuery<WeatherForecastProjection, GetWeatherForecastCountQuery, WeatherForecastCountResult>,
      IWaitForSortableUniqueId
{
    string? IWaitForSortableUniqueId.WaitForSortableUniqueId => WaitForSortableUniqueId;
}
// Source: internalUsages/Dcb.Domain/Queries/GetWeatherForecastCountQuery.cs
```

`OrleansDcbExecutor` polls the MultiProjection grain until it reports having processed the requested sortable id
(`src/Sekiban.Dcb.Orleans/OrleansDcbExecutor.cs`). Timeouts are adaptive based on how old the id is, keeping UX snappy.

## Ad-hoc Tag State Reads

If you only need data for a single tag, call `GetTagStateAsync` directly:

```csharp
var tagId = TagStateId.From<StudentProjector>(new StudentTag(studentId));
var stateResult = await executor.GetTagStateAsync(tagId);
```

This bypasses MultiProjection entirely and is ideal for debugging or administrative tools.

## JSON Contracts

Projection payloads are serialized using the `JsonSerializerOptions` registered with `DcbDomainTypes`. Keep query result
records simple and version them carefully; changes flow directly to clients.

## Testing Queries

Use the in-memory executor with a seeded event store (`src/Sekiban.Dcb.InMemory`) to exercise queries without Orleans.
Replay events, run the projection, and assert on the resulting `ListQueryResult`.
