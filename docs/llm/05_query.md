# Query - Sekiban Event Sourcing

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Aggregate, Projector, Command and Events](03_aggregate_command_events.md)
> - [Multiple Aggregate Projector](04_multiple_aggregate_projector.md)
> - [Query](05_query.md) (You are here)
> - [Workflow](06_workflow.md)
> - [JSON and Orleans Serialization](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md)
> - [Client API (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md)
> - [Unit Testing](11_unit_testing.md)
> - [Common Issues and Solutions](12_common_issues.md)
> - [ResultBox](13_result_box.md)

## Query (Data Retrieval)

Sekiban supports two types of queries: List Queries and Non-List Queries.

### List Query

List Queries return collections of items and support filtering and sorting operations.

```csharp
using Orleans.Serialization.Attributes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
using Sekiban.Pure.ResultBoxes;
using System;
using System.Collections.Generic;
using System.Linq;

[GenerateSerializer]
public record YourListQuery(string FilterParameter = null)
    : IMultiProjectionListQuery<AggregateListProjector<YourAggregateProjector>, YourListQuery, YourListQuery.ResultRecord>
{
    public static ResultBox<IEnumerable<ResultRecord>> HandleFilter(
        MultiProjectionState<AggregateListProjector<YourAggregateProjector>> projection, 
        YourListQuery query, 
        IQueryContext context)
    {
        return projection.Payload.Aggregates
            .Where(m => m.Value.GetPayload() is YourAggregate)
            .Select(m => ((YourAggregate)m.Value.GetPayload(), m.Value.PartitionKeys))
            .Select(tuple => new ResultRecord(tuple.PartitionKeys.AggregateId, ...other properties...))
            .ToResultBox();
    }

    public static ResultBox<IEnumerable<ResultRecord>> HandleSort(
        IEnumerable<ResultRecord> filteredList, 
        YourListQuery query, 
        IQueryContext context)
    {
        return filteredList.OrderBy(m => m.SomeProperty).AsEnumerable().ToResultBox();
    }

    [GenerateSerializer]
    public record ResultRecord(Guid Id, ...other properties...);
}
```

**Required for List Queries**:
- Implement `IMultiProjectionListQuery<TProjection, TQuery, TResult>` interface
- Implement static `HandleFilter` method to filter data based on query parameters
- Implement static `HandleSort` method to sort the filtered results
- Define nested result record with `[GenerateSerializer]` attribute

#### Example: User List Query

```csharp
[GenerateSerializer]
public record ListUsersQuery(string? NameFilter = null, bool? ConfirmedOnly = null)
    : IMultiProjectionListQuery<AggregateListProjector<UserProjector>, ListUsersQuery, ListUsersQuery.ResultRecord>
{
    public static ResultBox<IEnumerable<ResultRecord>> HandleFilter(
        MultiProjectionState<AggregateListProjector<UserProjector>> projection, 
        ListUsersQuery query, 
        IQueryContext context)
    {
        var users = projection.Payload.Aggregates
            .Select(m => new { Key = m.Key, Payload = m.Value.GetPayload() })
            .Where(m => m.Payload is User || m.Payload is ConfirmedUser);
            
        // Apply name filter if provided
        if (!string.IsNullOrEmpty(query.NameFilter))
        {
            users = users.Where(m => 
                (m.Payload as User)?.Name?.Contains(query.NameFilter, StringComparison.OrdinalIgnoreCase) == true ||
                (m.Payload as ConfirmedUser)?.Name?.Contains(query.NameFilter, StringComparison.OrdinalIgnoreCase) == true);
        }
        
        // Apply confirmation filter if provided
        if (query.ConfirmedOnly == true)
        {
            users = users.Where(m => m.Payload is ConfirmedUser);
        }
        
        return users.Select(m => {
            var isConfirmed = m.Payload is ConfirmedUser;
            var user = (m.Payload as User) ?? (m.Payload as ConfirmedUser);
            return new ResultRecord(
                m.Key,
                user!.Name,
                user!.Email,
                isConfirmed);
        }).ToResultBox();
    }

    public static ResultBox<IEnumerable<ResultRecord>> HandleSort(
        IEnumerable<ResultRecord> filteredList, 
        ListUsersQuery query, 
        IQueryContext context)
    {
        return filteredList.OrderBy(m => m.Name).ToResultBox();
    }

    [GenerateSerializer]
    public record ResultRecord(Guid Id, string Name, string Email, bool IsConfirmed);
}
```

### Non-List Query

Non-List Queries return a single result and are typically used for checking conditions or retrieving specific values.

```csharp
[GenerateSerializer]
public record YourNonListQuery(string Parameter)
    : IMultiProjectionQuery<AggregateListProjector<YourAggregateProjector>, YourNonListQuery, bool>
{
    public static ResultBox<bool> HandleQuery(
        MultiProjectionState<AggregateListProjector<YourAggregateProjector>> projection,
        YourNonListQuery query,
        IQueryContext context)
    {
        return projection.Payload.Aggregates.Values
            .Any(aggregate => SomeCondition(aggregate, query.Parameter));
    }
    
    private static bool SomeCondition(Aggregate aggregate, string parameter)
    {
        // Your condition logic here
        return aggregate.GetPayload() is YourAggregate payload && 
               payload.SomeProperty == parameter;
    }
}
```

**Required for Non-List Queries**:
- Implement `IMultiProjectionQuery<TProjection, TQuery, TResult>` interface
- Implement static `HandleQuery` method that returns a single result
- The result type can be any serializable type (bool, string, int, custom record, etc.)

#### Example: User Details Query

```csharp
[GenerateSerializer]
public record GetUserDetailsQuery(Guid UserId)
    : IMultiProjectionQuery<AggregateListProjector<UserProjector>, GetUserDetailsQuery, GetUserDetailsQuery.UserDetails?>
{
    public static ResultBox<UserDetails?> HandleQuery(
        MultiProjectionState<AggregateListProjector<UserProjector>> projection,
        GetUserDetailsQuery query,
        IQueryContext context)
    {
        if (!projection.Payload.Aggregates.TryGetValue(query.UserId, out var aggregate))
        {
            return null;
        }
        
        var payload = aggregate.GetPayload();
        
        if (payload is User user)
        {
            return new UserDetails(
                query.UserId,
                user.Name,
                user.Email,
                isConfirmed: false,
                aggregate.Version,
                aggregate.LastModified);
        }
        
        if (payload is ConfirmedUser confirmedUser)
        {
            return new UserDetails(
                query.UserId,
                confirmedUser.Name,
                confirmedUser.Email,
                isConfirmed: true,
                aggregate.Version,
                aggregate.LastModified);
        }
        
        return null;
    }
    
    [GenerateSerializer]
    public record UserDetails(
        Guid Id,
        string Name,
        string Email,
        bool IsConfirmed,
        int Version,
        DateTime LastModified);
}
```

## Advanced Query Features

### Waiting for Specific Events with IWaitForSortableUniqueId

When building real-time UI applications with event sourcing, there's often a lag between command execution and when the updated state is available for queries. Sekiban solves this with the `IWaitForSortableUniqueId` interface.

```csharp
// Define a query that can wait for specific events
[GenerateSerializer]
public record YourQuery(string QueryParam) : 
    IMultiProjectionQuery<YourProjection, YourQuery, YourResult>,
    IWaitForSortableUniqueId
{
    // Implement the interface property
    public string? WaitForSortableUniqueId { get; set; }
    
    // Query handling implementation
    public static ResultBox<YourResult> HandleQuery(
        MultiProjectionState<YourProjection> state,
        YourQuery query,
        IQueryContext context)
    {
        // Your query logic here
    }
}
```

**Required for Wait-Enabled Queries**:
- Implement the `IWaitForSortableUniqueId` interface
- Add a public property `WaitForSortableUniqueId` with getter and setter
- The property should be nullable string type

#### Implementation Example: API Endpoints

```csharp
// In Program.cs
apiRoute.MapGet("/your-endpoint",
    async ([FromQuery] string? waitForSortableUniqueId, [FromServices] SekibanOrleansExecutor executor) =>
    {
        var query = new YourQuery("parameter") 
        {
            WaitForSortableUniqueId = waitForSortableUniqueId
        };
        return await executor.QueryAsync(query).UnwrapBox();
    });
```

#### Implementation Example: Client-Side

```csharp
// API Client implementation
public async Task<YourResult> GetResultAsync(string? waitForSortableUniqueId = null)
{
    var requestUri = string.IsNullOrEmpty(waitForSortableUniqueId)
        ? "/api/your-endpoint"
        : $"/api/your-endpoint?waitForSortableUniqueId={Uri.EscapeDataString(waitForSortableUniqueId)}";
    
    // Make the HTTP request
}

// Using the client after executing a command
var commandResult = await client.ExecuteCommandAsync(new YourCommand());
var updatedResult = await client.GetResultAsync(commandResult.LastSortableUniqueId);
```

**Key Points**:
- The `LastSortableUniqueId` is available in command execution results
- Pass this ID to subsequent queries to ensure they see the updated state
- This provides immediate consistency in your application UI

### Pagination in List Queries

For large datasets, you can implement pagination in your list queries:

```csharp
[GenerateSerializer]
public record PaginatedListQuery(int Page = 1, int PageSize = 10, string? SearchTerm = null)
    : IMultiProjectionListQuery<AggregateListProjector<YourProjector>, PaginatedListQuery, PaginatedListQuery.ResultRecord>
{
    public static ResultBox<IEnumerable<ResultRecord>> HandleFilter(
        MultiProjectionState<AggregateListProjector<YourProjector>> projection, 
        PaginatedListQuery query, 
        IQueryContext context)
    {
        var filtered = projection.Payload.Aggregates
            .Where(m => m.Value.GetPayload() is YourAggregate);
            
        if (!string.IsNullOrEmpty(query.SearchTerm))
        {
            filtered = filtered.Where(m => 
                ((YourAggregate)m.Value.GetPayload()).Name.Contains(query.SearchTerm));
        }
        
        // Skip and Take for pagination (happens after filtering)
        int skip = (query.Page - 1) * query.PageSize;
        
        return filtered
            .Select(m => new ResultRecord(
                m.Key, 
                ((YourAggregate)m.Value.GetPayload()).Name))
            .Skip(skip)
            .Take(query.PageSize)
            .ToResultBox();
    }
    
    // Other required methods...
    
    [GenerateSerializer]
    public record ResultRecord(Guid Id, string Name);
}
```

When implementing pagination in your API endpoint:

```csharp
apiRoute.MapGet("/items",
    async ([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? searchTerm = null,
            [FromServices] SekibanOrleansExecutor executor) =>
    {
        var query = new PaginatedListQuery(page, pageSize, searchTerm);
        var result = await executor.QueryAsync(query).UnwrapBox();
        
        // You might want to include total count for UI pagination
        var countQuery = new CountItemsQuery(searchTerm);
        var totalCount = await executor.QueryAsync(countQuery).UnwrapBox();
        
        return new {
            Items = result,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    });
```