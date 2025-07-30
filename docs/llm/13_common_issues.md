# Common Issues and Solutions - Sekiban Event Sourcing

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Aggregate, Projector, Command and Events](03_aggregate_command_events.md)
> - [Multiple Aggregate Projector](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Workflow](06_workflow.md)
> - [JSON and Orleans Serialization](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md)
> - [Client API (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md)
> - [Dapr Setup](11_dapr_setup.md)
> - [Unit Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md) (You are here)
> - [ResultBox](14_result_box.md)
> - [Value Object](15_value_object.md)
> - [Deployment Guide](16_deployment.md)

## Common Issues and Solutions

This section covers common issues you might encounter when working with Sekiban and their solutions.

## 1. Namespace Errors

**Issue**: Compiler errors due to incorrect namespaces.

**Solution**: Make sure to use `Sekiban.Pure.*` namespaces, not `Sekiban.Core.*`. The most common namespaces are:

```csharp
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Query;
using Sekiban.Pure.ResultBoxes;
```

## 2. Command Context Errors

**Issue**: Cannot access aggregate payload directly from command context.

**Solution**: The command context doesn't directly expose the aggregate payload. Use pattern matching:

```csharp
public ResultBox<EventOrNone> Handle(YourCommand command, ICommandContext<IAggregatePayload> context)
{
    if (context.GetAggregate().GetPayload() is YourAggregate aggregate)
    {
        // Now you can use aggregate properties
        var property = aggregate.Property;
        
        return EventOrNone.Event(new YourEvent(...));
    }
    
    return new SomeException("Expected YourAggregate");
}
```

Or use the three-parameter version of `ICommandWithHandler` for stronger typing:

```csharp
public record YourCommand(...) 
    : ICommandWithHandler<YourCommand, YourProjector, YourAggregateType>
{
    // Now the context is typed to ICommandContext<YourAggregateType>
    public ResultBox<EventOrNone> Handle(YourCommand command, ICommandContext<YourAggregateType> context)
    {
        var aggregate = context.GetAggregate();
        // Payload is already typed as YourAggregateType
        var payload = aggregate.Payload;
        
        return EventOrNone.Event(new YourEvent(...));
    }
}
```

## 3. Time Unified Acquisition - SekibanDateProducer

**Issue**: Time acquisition methods are not unified within and outside the Sekiban system.

**Solution**: Use `SekibanDateProducer` to obtain unified time across the entire system:

```csharp
// Get time using the same method as Sekiban
var currentTime = SekibanDateProducer.GetRegistered().UtcNow;
```

This approach allows you to use the same time source for both Sekiban's event sourcing system and external systems. Time can also be mocked during testing.

## 4. Serialization Issues

**Issue**: `System.NotSupportedException: Orleans serialization requires types to be serializable.`

**Solution**: Ensure all types that are used in commands, events, and aggregates have the `[GenerateSerializer]` attribute:

```csharp
[GenerateSerializer]
public record YourCommand(...);

[GenerateSerializer]
public record YourEvent(...);

[GenerateSerializer]
public record YourAggregate(...);
```

For non-record types, use the `[Id]` attribute for fields and properties:

```csharp
public class ComplexType
{
    [Id(0)]
    public string PropertyA { get; set; } = null!;

    [Id(1)]
    public int PropertyB { get; set; }
}
```

## 5. Source Generation Issues

**Issue**: Missing `YourProjectDomainDomainTypes` class.

**Solution**: 

1. Ensure your project compiles successfully
2. Check that your domain types are correctly defined with the required attributes
3. Use the correct namespace for the generated types: `using YourProject.Domain.Generated;`
4. Rebuild the solution to trigger source generation

## 6. Query Result Issues

**Issue**: Query returns empty or stale results after executing a command.

**Solution**: Use the `IWaitForSortableUniqueId` interface to ensure consistency:

```csharp
// When executing a command, get the LastSortableUniqueId
var commandResult = await executor.CommandAsync(new YourCommand(...)).UnwrapBox();
var lastSortableId = commandResult.LastSortableUniqueId;

// Then pass it to your query
var query = new YourQuery(...) { WaitForSortableUniqueId = lastSortableId };
var queryResult = await executor.QueryAsync(query).UnwrapBox();
```

## 7. Multiple Events from Command

**Issue**: Need to return multiple events from a command handler.

**Solution**: Use the `AppendEvent` method and return `EventOrNone.None`:

```csharp
public ResultBox<EventOrNone> Handle(ComplexCommand command, ICommandContext<TAggregatePayload> context)
{
    context.AppendEvent(new FirstEventHappened(command.SomeData));
    context.AppendEvent(new SecondEventHappened(command.OtherData));
    
    return EventOrNone.None;  // Indicates all events have been appended
}
```

## 8. Orleans Clustering Issues

**Issue**: Orleans silo cannot connect to cluster.

**Solution**: Check your clustering configuration:

```csharp
// For development
siloBuilder.UseLocalhostClustering();

// For production with Azure Storage
siloBuilder.UseAzureStorageClustering(options =>
{
    options.ConfigureTableServiceClient(connectionString);
});

// For Kubernetes
siloBuilder.UseKubernetesHosting();
```

And ensure connection strings are correctly configured.

## 9. Database Configuration

**Issue**: Application cannot connect to the database.

**Solution**: Check your appsettings.json configuration:

```json
{
  "Sekiban": {
    "Database": "Cosmos",  // or "Postgres"
    "Cosmos": {
      "ConnectionString": "your-connection-string",
      "DatabaseName": "your-database-name"
    }
  }
}
```

And ensure your database setup is correct:

```csharp
// For Cosmos DB
builder.AddSekibanCosmosDb();

// For PostgreSQL
builder.AddSekibanPostgresDb();
```

## 10. Testing Issues

**Issue**: Tests fail with serialization exceptions.

**Solution**: Use the `CheckSerializability` method to test serialization:

```csharp
[Fact]
public void TestSerializable()
{
    CheckSerializability(new YourCommand(...));
    CheckSerializability(new YourEvent(...));
}
```

## 11. Performance Issues

**Issue**: Slow performance with large event streams.

**Solution**: 

1. Consider implementing event snapshots
2. Use appropriate database indexing
3. Optimize your queries for specific read patterns
4. Consider using multiple projections for different query needs
5. Use pagination for large result sets

## 12. Concurrency Issues

**Issue**: Command fails with concurrency exceptions.

**Solution**: Implement optimistic concurrency control:

```csharp
public ResultBox<EventOrNone> Handle(YourCommand command, ICommandContext<YourAggregateType> context)
{
    // Check the expected version
    if (context.GetAggregate().Version != command.ExpectedVersion)
    {
        return new ConcurrencyException(
            $"Expected version {command.ExpectedVersion} but found {context.GetAggregate().Version}");
    }
    
    // Continue with command handling
    return EventOrNone.Event(new YourEvent(...));
}
```

## 13. API Endpoint Issues

**Issue**: API endpoints return 500 Internal Server Error.

**Solution**: Improve error handling in your endpoints:

```csharp
apiRoute.MapPost("/command",
    async ([FromBody] YourCommand command, [FromServices] SekibanOrleansExecutor executor) =>
    {
        try
        {
            var result = await executor.CommandAsync(command);
            return result.Match(
                success => Results.Ok(success),
                error => Results.Problem(error.Error.Message)
            );
        }
        catch (Exception ex)
        {
            // Log the exception
            return Results.Problem(
                title: "An unexpected error occurred",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    });
```

## 14. Dependency Injection Issues

**Issue**: `System.InvalidOperationException: No service for type 'YourDomainDomainTypes'`

**Solution**: Make sure to register your domain types in Program.cs:

```csharp
builder.Services.AddSingleton(
    YourDomainDomainTypes.Generate(
        YourDomainEventsJsonContext.Default.Options));
```

## 15. Running the Application

**Issue**: Issues running the application with the Aspire host.

**Solution**: Use the following command:

```bash
dotnet run --project MyProject.AppHost
```

To launch with HTTPS profile:

```bash
dotnet run --project MyProject.AppHost --launch-profile https
```

## 16. ISekibanExecutor vs. SekibanOrleansExecutor

**Issue**: Not sure which executor type to use.

**Solution**: When implementing domain services or workflows, use the `ISekibanExecutor` interface instead of the concrete `SekibanOrleansExecutor` class for better testability. The `ISekibanExecutor` interface is in the `Sekiban.Pure.Executors` namespace.

```csharp
// Better for testability
public static async Task<Result> YourWorkflow(
    YourCommand command,
    ISekibanExecutor executor)
{
    // Implementation
}

// Instead of
public static async Task<Result> YourWorkflow(
    YourCommand command,
    SekibanOrleansExecutor executor)
{
    // Implementation
}
```
