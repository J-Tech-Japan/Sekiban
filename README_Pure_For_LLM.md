# Sekiban Event Sourcing - LLM Implementation Guide

## Getting Started

```bash
# Install the Sekiban templates
dotnet new install Sekiban.Pure.Templates

# Create a new project
dotnet new sekiban-orleans-aspire -n MyProject
```

This template includes Aspire host for Orleans, Cluster Storage, Grain Persistent Storage, and Queue Storage.

## Important Notes

### Correct Namespaces
The template uses the `Sekiban.Pure.*` namespace hierarchy, not `Sekiban.Core.*`. Always use the following namespaces:

- `Sekiban.Pure.Aggregates` for aggregates and payload interfaces
- `Sekiban.Pure.Events` for events
- `Sekiban.Pure.Projectors` for projectors
- `Sekiban.Pure.Command.Handlers` for command handlers
- `Sekiban.Pure.Command.Executor` for command execution context
- `Sekiban.Pure.Documents` for partition keys
- `Sekiban.Pure.Query` for queries
- `ResultBoxes` for result handling

### Project Structure
The template creates a solution with multiple projects:
- `MyProject.Domain` - Contains domain models, events, commands, and queries
- `MyProject.ApiService` - API endpoints for commands and queries
- `MyProject.Web` - Web frontend with Blazor
- `MyProject.AppHost` - Aspire host for orchestrating services
- `MyProject.ServiceDefaults` - Common service configurations

### Running the Application
When running the application with the Aspire host, use the following command:

```bash
dotnet run --project MyProject.AppHost
```

## Core Concepts

Event Sourcing: Store all state changes as immutable events. Current state is derived by replaying events.

## Essential Interfaces & Implementation Requirements

### 1. Aggregate (Domain Entity)

```csharp
[GenerateSerializer]
public record YourAggregate(...properties...) : IAggregatePayload
{
    // Domain logic methods
}
```

**Required**:
- Implement `IAggregatePayload` interface
- Use C# record for immutability
- Add `[GenerateSerializer]` attribute for Orleans

### 2. Commands (User Intentions)

```csharp
[GenerateSerializer]
public record YourCommand(...parameters...) 
    : ICommandWithHandler<YourCommand, YourAggregateProjector>
{
    // Required methods
    // For new aggregates:
    public PartitionKeys SpecifyPartitionKeys(YourCommand command) => 
        PartitionKeys.Generate<YourAggregateProjector>();
        
    // For existing aggregates:
    // public PartitionKeys SpecifyPartitionKeys(YourCommand command) => 
    //    PartitionKeys.Existing<YourAggregateProjector>(command.AggregateId);

    public ResultBox<EventOrNone> Handle(YourCommand command, ICommandContext<IAggregatePayload> context)
        => EventOrNone.Event(new YourEvent(...parameters...));
}
```

**Required**:
- Implement `ICommandWithHandler<TCommand, TProjector>` interface or `ICommandWithHandler<TCommand, TProjector, TPayloadType>` when you need to enforce state-based constraints
- Implement `SpecifyPartitionKeys` method:
  - For new aggregates: `PartitionKeys.Generate<YourProjector>()`
  - For existing aggregates: `PartitionKeys.Existing<YourProjector>(aggregateId)`
- Implement `Handle` method that returns events
- Add `[GenerateSerializer]` attribute

#### Using the Third Generic Parameter for State Constraints

You can specify a third generic parameter to enforce state-based constraints at the type level:

```csharp
[GenerateSerializer]
public record RevokeUser(Guid UserId) 
    : ICommandWithHandler<RevokeUser, UserProjector, ConfirmedUser>
{
    public PartitionKeys SpecifyPartitionKeys(RevokeUser command) => 
        PartitionKeys<UserProjector>.Existing(UserId);
    
    public ResultBox<EventOrNone> Handle(RevokeUser command, ICommandContext<ConfirmedUser> context) =>
        context
            .GetAggregate()
            .Conveyor(_ => EventOrNone.Event(new UserUnconfirmed()));
}
```

**Benefits**:
- The third generic parameter (`ConfirmedUser` in the example) specifies that this command can only be executed when the current aggregate payload is of that specific type
- The command context becomes strongly typed to `ICommandContext<ConfirmedUser>` instead of `ICommandContext<IAggregatePayload>`
- Provides compile-time safety for state-dependent operations
- The executor automatically checks if the current payload type matches the specified type before executing the command
- Particularly useful when using aggregate payload types to express different states of an entity (state machine pattern)

#### Accessing Aggregate Payload in Commands

There are two ways to access the aggregate payload in command handlers, depending on whether you use the two or three generic parameter version:

1. **With Type Constraint (Three Generic Parameters)**:
   ```csharp
   // Using ICommandWithHandler<TCommand, TProjector, TAggregatePayload>
   public ResultBox<EventOrNone> Handle(YourCommand command, ICommandContext<ConfirmedUser> context)
   {
       // Direct access to strongly-typed aggregate and payload
       var aggregate = context.GetAggregate();
       var payload = aggregate.Payload; // Already typed as ConfirmedUser
       
       // Use payload properties directly
       var userName = payload.Name;
       
       return EventOrNone.Event(new YourEvent(...));
   }
   ```

2. **Without Type Constraint (Two Generic Parameters)**:
   ```csharp
   // Using ICommandWithHandler<TCommand, TProjector>
   public ResultBox<EventOrNone> Handle(YourCommand command, ICommandContext<IAggregatePayload> context)
   {
       // Need to cast the payload to the expected type
       if (context.GetAggregate().GetPayload() is ConfirmedUser payload)
       {
           // Now you can use the typed payload
           var userName = payload.Name;
           
           return EventOrNone.Event(new YourEvent(...));
       }
       
       // Handle case where payload is not of expected type
       return new SomeException("Expected ConfirmedUser state");
   }
   ```

The three-parameter version is preferred when you know the exact state the aggregate should be in, as it provides compile-time safety and cleaner code.

#### Generating Multiple Events from a Command

If a command needs to generate multiple events, you can use the `AppendEvent` method on the command context:

```csharp
public ResultBox<EventOrNone> Handle(ComplexCommand command, ICommandContext<TAggregatePayload> context)
{
    // First, append events one by one
    context.AppendEvent(new FirstEventHappened(command.SomeData));
    context.AppendEvent(new SecondEventHappened(command.OtherData));
    
    // Then return EventOrNone.None to indicate that all events have been appended
    return EventOrNone.None;
    
    // Alternatively, you can return the last event
    // return EventOrNone.Event(new FinalEventHappened(command.FinalData));
}
```

**Key points**:
- Use `context.AppendEvent(eventPayload)` to add events to the event stream
- You can append multiple events in sequence
- Return `EventOrNone.None` if all events have been appended using `AppendEvent`
- Or return the last event using `EventOrNone.Event` if you prefer that approach
- All appended events will be applied to the aggregate in the order they were added

### 3. Events (Facts That Happened)

Events contain two parts:

1. **Event Metadata** (handled by Sekiban):
   ```csharp
   // These are managed by the system
   PartitionKeys partitionKeys;
   DateTime timestamp;
   Guid id;
   int version;
   // Other system metadata
   ```

2. **Event Payload** (defined by developers):
   ```csharp
   [GenerateSerializer]
   public record YourEvent(...parameters...) : IEventPayload;
   ```

**Required**:
- Implement `IEventPayload` interface for domain-specific data only
- Use past tense naming (Created, Updated, Deleted)
- Add `[GenerateSerializer]` attribute
- Include all data needed to reconstruct domain state

### PartitionKeys Structure

```csharp
public class PartitionKeys
{
    string RootPartitionKey;  // Optional tenant key for multi-tenancy
    string AggregateGroup;    // Usually projector name
    Guid AggregateId;        // Unique identifier
}
```

**Usage in Commands**:
```csharp
// For new aggregates:
public PartitionKeys SpecifyPartitionKeys(YourCommand command) => 
    PartitionKeys.Generate<YourProjector>();

// For existing aggregates:
public PartitionKeys SpecifyPartitionKeys(YourCommand command) => 
    PartitionKeys.Existing<YourProjector>(command.AggregateId);
```

### 4. Projector (State Builder)

```csharp
// Example of state transitions in projector
public class UserProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev)
        => (payload, ev.GetPayload()) switch
        {
            // Each case can return a different payload type to represent state
            (EmptyAggregatePayload, UserRegistered e) => new UnconfirmedUser(e.Name, e.Email),
            (UnconfirmedUser user, UserConfirmed _) => new ConfirmedUser(user.Name, user.Email),
            (ConfirmedUser user, UserUnconfirmed _) => new UnconfirmedUser(user.Name, user.Email),
            _ => payload
        };
}

// Different states enable different operations
public record UnconfirmedUser(string Name, string Email) : IAggregatePayload;
public record ConfirmedUser(string Name, string Email) : IAggregatePayload;
```

**Required**:
- Implement `IAggregateProjector` interface
- Use pattern matching to manage state transitions
- Return different payload types to enforce business rules
- Handle initial state creation from `EmptyAggregatePayload`
- Maintain immutability in state changes

### 5. Query (Data Retrieval)

Sekiban supports two types of queries: List Queries and Non-List Queries.

#### List Query

List Queries return collections of items and support filtering and sorting operations.

```csharp
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

#### Non-List Query

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

### 6. JSON Context (For AOT Compilation)

```csharp
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EventDocumentCommon))]
[JsonSerializable(typeof(EventDocumentCommon[]))]
[JsonSerializable(typeof(EventDocument<YourEvent>))]
[JsonSerializable(typeof(YourEvent))]
// Add all event types
public partial class YourDomainEventsJsonContext : JsonSerializerContext
{
}
```

**Required**:
- Include all event types
- Add `[JsonSourceGenerationOptions]` attribute
- Define as partial class

## API Implementation

### Basic Setup Pattern

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// 1. Configure Orleans
builder.UseOrleans(config =>
{
    // set your own orleans settings
});

// 2. Register Domain
builder.Services.AddSingleton(
    BookManagementDomainDomainTypes.Generate(
        BookManagementDomainEventsJsonContext.Default.Options));

// 3. Configure Database
builder.AddSekibanCosmosDb();  // or AddSekibanPostgresDb();

// 4. Map Endpoints
var app = builder.Build();
var apiRoute = app.MapGroup("/api");

// Command endpoint pattern
apiRoute.MapPost("/command",
    async ([FromBody] YourCommand command, 
           [FromServices] SekibanOrleansExecutor executor) => 
        await executor.CommandAsync(command).UnwrapBox());

// Query endpoint pattern
apiRoute.MapGet("/query",
    async ([FromServices] SekibanOrleansExecutor executor) =>
    {
        var result = await executor.QueryAsync(new YourQuery())
                                  .UnwrapBox();
        return result.Items;
    });
```

### Implementation Steps

1. Define aggregate implementing `IAggregatePayload`
2. Create events implementing `IEventPayload`
3. Implement projector with `IAggregateProjector`
4. Create commands with `ICommandWithHandler<TCommand, TProjector>`
5. Define queries with appropriate query interface
6. Set up JSON serialization context
7. Configure Program.cs using the pattern above
8. Map endpoints for your commands and queries

### Configuration

```json
{
  "Sekiban": {
    "Database": "Cosmos"  // or "Postgres"
  }
}
```

## Web Frontend Implementation

To implement a web frontend for your domain:

1. Create an API client in the Web project:
```csharp
public class YourApiClient(HttpClient httpClient)
{
    public async Task<YourQuery.ResultRecord[]> GetItemsAsync(
        CancellationToken cancellationToken = default)
    {
        List<YourQuery.ResultRecord>? items = null;

        await foreach (var item in httpClient.GetFromJsonAsAsyncEnumerable<YourQuery.ResultRecord>("/api/items", cancellationToken))
        {
            if (item is not null)
            {
                items ??= [];
                items.Add(item);
            }
        }

        return items?.ToArray() ?? [];
    }

    public async Task CreateItemAsync(
        string param1,
        string param2,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateYourItemCommand(param1, param2);
        await httpClient.PostAsJsonAsync("/api/createitem", command, cancellationToken);
    }
}
```

2. Register the API client in Program.cs:
```csharp
builder.Services.AddHttpClient<YourApiClient>(client =>
{
    client.BaseAddress = new("https+http://apiservice");
});
```

3. Create Razor pages to interact with your domain

## Naming Conventions

- Commands: Imperative verbs (Create, Update, Delete)
- Events: Past tense verbs (Created, Updated, Deleted)
- Aggregates: Nouns representing domain entities
- Projectors: Named after the aggregate they project

## File Structure

```
YourProject.Domain/
├── YourAggregate.cs                    // Aggregate
├── YourAggregateProjector.cs           // Projector
├── CreateYourAggregateCommand.cs       // Command
├── UpdateYourAggregateCommand.cs       // Command
├── DeleteYourAggregateCommand.cs       // Command
├── YourAggregateCreated.cs             // Event
├── YourAggregateUpdated.cs             // Event
├── YourAggregateDeleted.cs             // Event
├── YourAggregateQuery.cs               // Query
└── YourDomainEventsJsonContext.cs      // JSON Context
```

## Unit Testing

Sekiban provides several approaches for unit testing your event-sourced applications. You can choose between in-memory testing for simplicity or Orleans-based testing for more complex scenarios.

### 1. In-Memory Testing with SekibanInMemoryTestBase

The simplest approach uses the `SekibanInMemoryTestBase` class from the `Sekiban.Pure.xUnit` namespace:

```csharp
public class YourTests : SekibanInMemoryTestBase
{
    // Override to provide your domain types
    protected override SekibanDomainTypes GetDomainTypes() => 
        YourDomainTypes.Generate(YourEventsJsonContext.Default.Options);

    [Fact]
    public void SimpleTest()
    {
        // Given - Execute a command and get the response
        var response1 = GivenCommand(new CreateYourEntity("Name", "Value"));
        Assert.Equal(1, response1.Version);

        // When - Execute another command on the same aggregate
        var response2 = WhenCommand(new UpdateYourEntity(response1.PartitionKeys.AggregateId, "NewValue"));
        Assert.Equal(2, response2.Version);

        // Then - Get the aggregate and verify its state
        var aggregate = ThenGetAggregate<YourEntityProjector>(response2.PartitionKeys);
        var entity = (YourEntity)aggregate.Payload;
        Assert.Equal("NewValue", entity.Value);
        
        // Then - Execute a query and verify the result
        var queryResult = ThenQuery(new YourEntityExistsQuery("Name"));
        Assert.True(queryResult);
    }
}
```

### 2. Method Chaining with ResultBox

For more fluent tests, you can use the ResultBox-based methods that support method chaining:

```csharp
public class YourTests : SekibanInMemoryTestBase
{
    protected override SekibanDomainTypes GetDomainTypes() => 
        YourDomainTypes.Generate(YourEventsJsonContext.Default.Options);

    [Fact]
    public void ChainedTest()
        => GivenCommandWithResult(new CreateYourEntity("Name", "Value"))
            .Do(response => Assert.Equal(1, response.Version))
            .Conveyor(response => WhenCommandWithResult(new UpdateYourEntity(response.PartitionKeys.AggregateId, "NewValue")))
            .Do(response => Assert.Equal(2, response.Version))
            .Conveyor(response => ThenGetAggregateWithResult<YourEntityProjector>(response.PartitionKeys))
            .Conveyor(aggregate => aggregate.Payload.ToResultBox().Cast<YourEntity>())
            .Do(payload => Assert.Equal("NewValue", payload.Value))
            .Conveyor(_ => ThenQueryWithResult(new YourEntityExistsQuery("Name")))
            .Do(Assert.True)
            .UnwrapBox();
}
```

Key points:
- `Conveyor` is used to chain operations, transforming the result of one operation into the input for the next
- `Do` is used to perform assertions or side effects without changing the result
- `UnwrapBox` at the end unwraps the final ResultBox, throwing an exception if any step failed

### 3. Orleans Testing with SekibanOrleansTestBase

For testing with Orleans integration, use the `SekibanOrleansTestBase` class from the `Sekiban.Pure.Orleans.xUnit` namespace:

```csharp
public class YourOrleansTests : SekibanOrleansTestBase<YourOrleansTests>
{
    public override SekibanDomainTypes GetDomainTypes() => 
        YourDomainTypes.Generate(YourEventsJsonContext.Default.Options);

    [Fact]
    public void OrleansTest() =>
        GivenCommandWithResult(new CreateYourEntity("Name", "Value"))
            .Do(response => Assert.Equal(1, response.Version))
            .Conveyor(response => WhenCommandWithResult(new UpdateYourEntity(response.PartitionKeys.AggregateId, "NewValue")))
            .Do(response => Assert.Equal(2, response.Version))
            .Conveyor(response => ThenGetAggregateWithResult<YourEntityProjector>(response.PartitionKeys))
            .Conveyor(aggregate => aggregate.Payload.ToResultBox().Cast<YourEntity>())
            .Do(payload => Assert.Equal("NewValue", payload.Value))
            .Conveyor(_ => ThenGetMultiProjectorWithResult<AggregateListProjector<YourEntityProjector>>())
            .Do(projector => 
            {
                Assert.Equal(1, projector.Aggregates.Values.Count());
                var entity = (YourEntity)projector.Aggregates.Values.First().Payload;
                Assert.Equal("NewValue", entity.Value);
            })
            .UnwrapBox();
            
    [Fact]
    public void TestSerializable()
    {
        // Test that commands are serializable (important for Orleans)
        CheckSerializability(new CreateYourEntity("Name", "Value"));
    }
}
```

### 4. Manual Testing with InMemorySekibanExecutor

For more complex scenarios or custom test setups, you can manually create an `InMemorySekibanExecutor`:

```csharp
[Fact]
public async Task ManualExecutorTest()
{
    // Create an in-memory executor
    var executor = new InMemorySekibanExecutor(
        YourDomainTypes.Generate(YourEventsJsonContext.Default.Options),
        new FunctionCommandMetadataProvider(() => "test"),
        new Repository(),
        new ServiceCollection().BuildServiceProvider());

    // Execute a command
    var result = await executor.CommandAsync(new CreateYourEntity("Name", "Value"));
    Assert.True(result.IsSuccess);
    var value = result.GetValue();
    Assert.NotNull(value);
    Assert.Equal(1, value.Version);
    var aggregateId = value.PartitionKeys.AggregateId;

    // Load the aggregate
    var aggregateResult = await executor.LoadAggregateAsync<YourEntityProjector>(
        PartitionKeys.Existing<YourEntityProjector>(aggregateId));
    Assert.True(aggregateResult.IsSuccess);
    var aggregate = aggregateResult.GetValue();
    var entity = (YourEntity)aggregate.Payload;
    Assert.Equal("Name", entity.Name);
    Assert.Equal("Value", entity.Value);
}
```

## SekibanDomainTypes and Source Generation

### Understanding SekibanDomainTypes

Sekiban uses source generation to create domain type registrations at build time. This is a key part of the framework that simplifies domain model registration and ensures type safety.

```csharp
// This class is automatically generated by Sekiban.Pure.SourceGenerator
// You don't need to create it manually
public static class YourProjectDomainDomainTypes
{
    // Used for registering domain types with the DI container
    public static SekibanDomainTypes Generate(JsonSerializerOptions options) => 
        // Implementation is generated based on your domain model
        ...

    // Used for serialization checking
    public static SekibanDomainTypes Generate() => 
        Generate(new JsonSerializerOptions());
}
```

### Key Points About Source Generation

1. **Naming Convention**:
   - The generated class follows the pattern `[ProjectName]DomainDomainTypes`
   - For example, a project named "SchoolManagement" will have `SchoolManagementDomainDomainTypes`

2. **Namespace**:
   - The generated class is placed in the `[ProjectName].Generated` namespace
   - For example, `SchoolManagement.Domain.Generated`

3. **Usage in Application**:
   ```csharp
   // In Program.cs
   builder.Services.AddSingleton(
       YourProjectDomainDomainTypes.Generate(
           YourProjectDomainEventsJsonContext.Default.Options));
   ```

4. **Usage in Tests**:
   ```csharp
   // In test classes
   protected override SekibanDomainTypes GetDomainTypes() => 
       YourProjectDomainDomainTypes.Generate(
           YourProjectDomainEventsJsonContext.Default.Options);
   ```

5. **Required Imports for Tests**:
   ```csharp
   using YourProject.Domain;
   using YourProject.Domain.Generated; // Contains the generated types
   using Sekiban.Pure;
   using Sekiban.Pure.xUnit;
   ```

### Troubleshooting Source Generation

1. **Missing Generated Types**:
   - Ensure the project builds successfully before running tests
   - Check that all domain types have the required attributes
   - Look for build warnings related to source generation

2. **Namespace Errors**:
   - Make sure to import the correct Generated namespace
   - The namespace is not visible in source files, only in compiled assemblies

3. **Type Not Found Errors**:
   - Ensure you're using the correct naming convention
   - Check for typos in the class name

4. **Testing Best Practices**:
   - Always reference the source-generated types directly
   - Don't create your own domain types class for testing
   - Use the same JsonSerializerOptions as the main application

### Testing Best Practices

1. **Test Commands**: Verify that commands produce the expected events and state changes
2. **Test Projectors**: Verify that projectors correctly apply events to build the aggregate state
3. **Test Queries**: Verify that queries return the expected results based on the current state
4. **Test State Transitions**: Verify that state transitions work correctly, especially when using different payload types
5. **Test Error Cases**: Verify that commands fail appropriately when validation fails
6. **Test Serialization**: For Orleans tests, verify that commands and events are serializable

## Common Issues and Solutions

1. **Namespace Errors**: Make sure to use `Sekiban.Pure.*` namespaces, not `Sekiban.Core.*`.

2. **Command Context**: The command context doesn't directly expose the aggregate payload. Use pattern matching in your command handlers if you need to check the aggregate state:
   ```csharp
   if (context.AggregatePayload is YourAggregate aggregate)
   {
       // Use aggregate properties
   }
   ```

3. **Running the Application**: When running the application with the Aspire host, you can use the following command:

```bash
dotnet run --project MyProject.AppHost
```

To launch the AppHost with HTTPS profile, use:

```bash
dotnet run --project MyProject.AppHost --launch-profile https
```

This ensures that your application uses HTTPS for secure communication, which is especially important for production environments.

4. **Accessing the Web Frontend**: The web frontend is available at the URL shown in the Aspire dashboard, typically at a URL like `https://localhost:XXXXX`.
