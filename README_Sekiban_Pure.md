# Sekiban Event Sourcing Guide for Developer
[For LLM](README_Sekiban_Pure_For_LLM.md)
[日本語版はこちら (Japanese Version)](README_Sekiban_Pure_JP.md)

This guide explains how to create and work with event sourcing projects using Sekiban, based on the template structure in `templates/Sekiban.Pure.Templates/content/Sekiban.Orleans.Aspire/OrleansSekiban.Domain`.

## Getting Started with Sekiban

To quickly create a new Sekiban project with Orleans and Aspire integration:

```bash
# Install the Sekiban templates
dotnet new install Sekiban.Pure.Templates

# Create a new project
dotnet new sekiban-orleans-aspire -n MyProject
```

This template includes:
- .NET Aspire host for Orleans
- Cluster Storage
- Grain Persistent Storage
- Queue Storage

## What is Event Sourcing?

Event Sourcing is a design pattern where:
- All changes to application state are stored as a sequence of events
- These events are the source of truth
- The current state is derived by replaying events
- Events are immutable and represent facts that happened in the system

## Sekiban Event Sourcing Framework

Sekiban is a .NET event sourcing framework that:
- Simplifies implementing event sourcing in C# applications
- Provides integration with Orleans for distributed systems
- Supports various storage backends
- Offers a clean, type-safe API for defining domain models

## Key Components of a Sekiban Project

### 1. Aggregate

An aggregate consists of two main parts:

1. **Aggregate Payload**: Basic information that exists in every aggregate, such as:
   - Current version
   - Last event ID
   - Other system-level metadata

2. **Payload**: The domain-specific data defined by developers.

In Sekiban, aggregates implement `IAggregatePayload`:

```csharp
[GenerateSerializer]
public record WeatherForecast(
    string Location,
    DateOnly Date,
    int TemperatureC,
    string Summary
) : IAggregatePayload
{
    public int GetTemperatureF()
    {
        return 32 + (int)(TemperatureC / 0.5556);
    }
}
```

Key points:
- Use C# records for immutability
- Implement `IAggregatePayload` interface for combining both aggregate payload and domain payload
- Include the `[GenerateSerializer]` attribute for Orleans serialization
- Define domain-specific properties as constructor parameters
- Include domain logic methods within the record

### 2. Commands

Commands represent user intentions to change the system state. They define what should happen.

```csharp
[GenerateSerializer]
public record InputWeatherForecastCommand(
    string Location,
    DateOnly Date,
    int TemperatureC,
    string Summary
) : ICommandWithHandler<InputWeatherForecastCommand, WeatherForecastProjector>
{
    public PartitionKeys SpecifyPartitionKeys(InputWeatherForecastCommand command) => 
        PartitionKeys.Generate<WeatherForecastProjector>();

    public ResultBox<EventOrNone> Handle(InputWeatherForecastCommand command, ICommandContext<IAggregatePayload> context)
        => EventOrNone.Event(new WeatherForecastInputted(command.Location, command.Date, command.TemperatureC, command.Summary));    
}
```

Key points:
- Use C# records for immutability
- Implement `ICommandWithHandler<TCommand, TProjector>` interface or `ICommandWithHandler<TCommand, TProjector, TPayloadType>` interface when you need to enforce state-based constraints
- Include the `[GenerateSerializer]` attribute
- Define `SpecifyPartitionKeys` method to determine where the aggregate is stored:
  - For new aggregates: `PartitionKeys.Generate<YourProjector>()`
  - For existing aggregates: `PartitionKeys.Existing<YourProjector>(aggregateId)`
- Implement `Handle` method that returns events or no events
- Commands don't modify state directly; they produce events

#### Using the Third Generic Parameter for State Constraints

You can specify a third generic parameter to enforce state-based constraints at the type level:

```csharp
public record RevokeUser(Guid UserId) : ICommandWithHandler<RevokeUser, UserProjector, ConfirmedUser>
{
    public PartitionKeys SpecifyPartitionKeys(RevokeUser command) => PartitionKeys<UserProjector>.Existing(UserId);
    
    public ResultBox<EventOrNone> Handle(RevokeUser command, ICommandContext<ConfirmedUser> context) =>
        context
            .GetAggregate()
            .Conveyor(_ => EventOrNone.Event(new UserUnconfirmed()));
}
```

Key points:
- The third generic parameter `ConfirmedUser` specifies that this command can only be executed when the current aggregate payload is of type `ConfirmedUser`
- The command context is now strongly typed to `ICommandContext<ConfirmedUser>` instead of `ICommandContext<IAggregatePayload>`
- This provides compile-time safety for state-dependent operations
- The executor will automatically check if the current payload type matches the specified type before executing the command
- This is particularly useful when using aggregate payload types to express different states of an entity

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

Key points:
- Use `context.AppendEvent(eventPayload)` to add events to the event stream
- You can append multiple events in sequence
- Return `EventOrNone.None` if all events have been appended using `AppendEvent`
- Or return the last event using `EventOrNone.Event` if you prefer that approach
- All appended events will be applied to the aggregate in the order they were added

### 3. Events

Events consist of two main parts:

1. **Event Metadata**: System-level information included in every event:
   - PartitionKeys
   - Timestamp
   - Id
   - Version
   - Other system metadata

2. **Event Payload**: The domain-specific data defined by developers.

Events are immutable and represent facts that have happened in the system. Developers focus on defining the Event Payload by implementing `IEventPayload`:

```csharp
[GenerateSerializer]
public record WeatherForecastInputted(
    string Location,
    DateOnly Date,
    int TemperatureC,
    string Summary
) : IEventPayload;
```

Key points:
- Use C# records for immutability
- Implement `IEventPayload` interface for the domain-specific event data
- Include the `[GenerateSerializer]` attribute
- Name events in past tense (e.g., "Inputted", "Updated", "Deleted")
- Include all data needed to reconstruct the state change

### Partition Keys

PartitionKeys define how data is organized in the database and consist of three components:

1. **RootPartitionKey** (string):
   - Can be used as a Tenant Key in multi-tenant applications
   - Helps segregate data by tenant or other high-level divisions

2. **AggregateGroup** (string):
   - Defines a group of aggregates
   - Usually matches the projector name
   - Used to organize related aggregates together

3. **AggregateId** (Guid):
   - Unique identifier for each aggregate instance
   - Used to locate specific aggregates within a group

When implementing commands, you use these partition keys in two ways:
- For new aggregates: `PartitionKeys.Generate<YourProjector>()` generates new partition keys
- For existing aggregates: `PartitionKeys.Existing<YourProjector>(aggregateId)` uses existing keys

### 4. Projectors

Projectors apply events to aggregates to build the current state. A key feature of projectors is their ability to change the aggregate payload type to express state transitions, which enables state-dependent behavior in commands.

Here's an example showing state transitions in a user registration flow:

```csharp
public class UserProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev) => (payload, ev.GetPayload()) switch
    {
        // Initial registration creates an UnconfirmedUser
        (EmptyAggregatePayload, UserRegistered registered) => new UnconfirmedUser(registered.Name, registered.Email),
        
        // Confirmation changes UnconfirmedUser to ConfirmedUser
        (UnconfirmedUser unconfirmedUser, UserConfirmed) => new ConfirmedUser(
            unconfirmedUser.Name,
            unconfirmedUser.Email),
            
        // Unconfirmation changes ConfirmedUser back to UnconfirmedUser
        (ConfirmedUser confirmedUser, UserUnconfirmed) => new UnconfirmedUser(confirmedUser.Name, confirmedUser.Email),
        
        _ => payload
    };
}
```

Key points:
- Implement `IAggregateProjector` interface
- Use pattern matching to handle different event types
- Return different aggregate payload types based on state transitions:
  - State changes can enforce business rules (e.g., only confirmed users can perform certain actions)
  - Commands can check the current state type to determine valid operations
  - The type system helps enforce business rules at compile time
- Handle the initial state creation (from `EmptyAggregatePayload`)
- Maintain immutability by creating new instances for each state change

### 5. Queries

Queries define how to retrieve and filter data from the system.

```csharp
[GenerateSerializer]
public record WeatherForecastQuery(string LocationContains)
    : IMultiProjectionListQuery<AggregateListProjector<WeatherForecastProjector>, WeatherForecastQuery, WeatherForecastQuery.WeatherForecastRecord>
{
    public static ResultBox<IEnumerable<WeatherForecastRecord>> HandleFilter(MultiProjectionState<AggregateListProjector<WeatherForecastProjector>> projection, WeatherForecastQuery query, IQueryContext context)
    {
        return projection.Payload.Aggregates.Where(m => m.Value.GetPayload() is WeatherForecast)
            .Select(m => ((WeatherForecast)m.Value.GetPayload(), m.Value.PartitionKeys))
            .Select((touple) => new WeatherForecastRecord(touple.PartitionKeys.AggregateId, touple.Item1.Location,
                touple.Item1.Date, touple.Item1.TemperatureC, touple.Item1.Summary, touple.Item1.GetTemperatureF()))
            .ToResultBox();
    }

    public static ResultBox<IEnumerable<WeatherForecastRecord>> HandleSort(IEnumerable<WeatherForecastRecord> filteredList, WeatherForecastQuery query, IQueryContext context)
    {
        return filteredList.OrderBy(m => m.Date).AsEnumerable().ToResultBox();
    }

    [GenerateSerializer]
    public record WeatherForecastRecord(
        Guid WeatherForecastId,
        string Location,
        DateOnly Date,
        int TemperatureC,
        string Summary,
        int TemperatureF
    );
}
```

Key points:
- Implement appropriate query interface (e.g., `IMultiProjectionListQuery`)
- Define filter and sort methods
- Create a nested record for query results
- Use LINQ for filtering and sorting
- Return results wrapped in `ResultBox`

### 6. JSON Serialization Context

For AOT compilation and performance, define a JSON serialization context.

```csharp
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EventDocumentCommon))]
[JsonSerializable(typeof(EventDocumentCommon[]))]
[JsonSerializable(typeof(EventDocument<OrleansSekiban.Domain.WeatherForecastInputted>))]
[JsonSerializable(typeof(OrleansSekiban.Domain.WeatherForecastInputted))]
[JsonSerializable(typeof(EventDocument<OrleansSekiban.Domain.WeatherForecastDeleted>))]
[JsonSerializable(typeof(OrleansSekiban.Domain.WeatherForecastDeleted))]
[JsonSerializable(typeof(EventDocument<OrleansSekiban.Domain.WeatherForecastLocationUpdated>))]
[JsonSerializable(typeof(OrleansSekiban.Domain.WeatherForecastLocationUpdated))]
public partial class OrleansSekibanDomainEventsJsonContext : JsonSerializerContext
{
}
```

Key points:
- Include all event types that need serialization
- Use `[JsonSourceGenerationOptions]` to configure serialization
- Define as a partial class

### Using ToSimpleCommandResponse() for Efficient API Endpoints

When creating API endpoints that execute commands, using the `ToSimpleCommandResponse()` extension method offers several benefits:

1. **Reduced Payload Size**: Converts the full CommandResponse (with all events) to a compact CommandResponseSimple
2. **Easy Access to LastSortableUniqueId**: Extracts the most important information for client-side consistency
3. **Clean API Design**: Combined with `UnwrapBox()`, creates clean, consistent API responses

#### Implementation Example

```csharp
apiRoute
    .MapPost(
        "/inputweatherforecast",
        async (
                [FromBody] InputWeatherForecastCommand command,
                [FromServices] SekibanOrleansExecutor executor) =>
            await executor.CommandAsync(command).ToSimpleCommandResponse().UnwrapBox())
    .WithName("InputWeatherForecast")
    .WithOpenApi();
```

#### Client-Side Usage

After executing a command, use the returned `LastSortableUniqueId` to ensure your subsequent queries see the updated state:

```csharp
// Execute a command
var response = await weatherApiClient.InputWeatherAsync(new InputWeatherForecastCommand(...));

// Use the LastSortableUniqueId for subsequent queries
var forecasts = await weatherApiClient.GetWeatherAsync(
    waitForSortableUniqueId: response.LastSortableUniqueId);
```

This pattern ensures that your UI always reflects the most recent state changes, providing a more consistent user experience.

## Project Structure

A typical Sekiban event sourcing project follows this structure:

```
YourProject.Domain/
├── Aggregates/
│   └── YourAggregate.cs
├── Commands/
│   ├── CreateYourAggregateCommand.cs
│   ├── UpdateYourAggregateCommand.cs
│   └── DeleteYourAggregateCommand.cs
├── Events/
│   ├── YourAggregateCreated.cs
│   ├── YourAggregateUpdated.cs
│   └── YourAggregateDeleted.cs
├── Projectors/
│   └── YourAggregateProjector.cs
├── Queries/
│   └── YourAggregateQuery.cs
└── YourProjectDomainEventsJsonContext.cs
```

## Best Practices for LLM Programming Agents

When working with Sekiban event sourcing projects:

1. **Understand the Domain Model**:
   - Identify the key aggregates and their relationships
   - Understand the business rules and constraints

2. **Follow the Event Sourcing Pattern**:
   - Commands validate and produce events
   - Events are immutable and represent facts
   - State is derived from events
   - Queries read from the projected state

3. **Naming Conventions**:
   - Commands: Imperative verbs (Create, Update, Delete)
   - Events: Past tense verbs (Created, Updated, Deleted)
   - Aggregates: Nouns representing domain entities
   - Projectors: Named after the aggregate they project

4. **Code Generation**:
   - Use the `[GenerateSerializer]` attribute for Orleans serialization
   - Implement the appropriate interfaces for each component
   - Use C# records for immutability

5. **Testing**:
   - Test commands by verifying the events they produce
   - Test projectors by applying events and checking the resulting state
   - Test queries by setting up test data and verifying the results
   - Use the built-in testing frameworks for in-memory or Orleans-based testing
   - Leverage method chaining with ResultBox for fluent test assertions

6. **Error Handling**:
   - Use `ResultBox` to handle errors and return meaningful messages
   - Validate commands before producing events
   - Handle edge cases in projectors

## Unit Testing in Sekiban

Sekiban provides robust support for unit testing your event-sourced applications with both in-memory and Orleans-based testing frameworks.

### In-Memory Testing

For simple unit tests, you can use the `SekibanInMemoryTestBase` class from the `Sekiban.Pure.xUnit` namespace:

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

The base class provides methods that follow the Given-When-Then pattern:
- `GivenCommand` - Sets up the initial state by executing a command
- `WhenCommand` - Executes the command being tested
- `ThenGetAggregate` - Retrieves an aggregate to verify its state
- `ThenQuery` - Executes a query to verify the result

### Method Chaining with ResultBox

For more fluent and readable tests, you can use the ResultBox-based methods that support method chaining:

```csharp
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
```

Key points:
- `Conveyor` transforms the result of one operation into the input for the next
- `Do` performs assertions or side effects without changing the result
- `UnwrapBox` unwraps the final ResultBox, throwing an exception if any step failed

### Orleans Testing

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

The Orleans test base class provides similar methods to the in-memory test base class but sets up a complete Orleans test cluster for more realistic testing.

### Manual Testing with InMemorySekibanExecutor

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

### Testing Best Practices

1. **Test Commands**: Verify that commands produce the expected events and state changes
2. **Test Projectors**: Verify that projectors correctly apply events to build the aggregate state
3. **Test Queries**: Verify that queries return the expected results based on the current state
4. **Test State Transitions**: Verify that state transitions work correctly, especially when using different payload types
5. **Test Error Cases**: Verify that commands fail appropriately when validation fails
6. **Test Serialization**: For Orleans tests, verify that commands and events are serializable

## Creating and Using a Sekiban Project

### 1. Project Setup

Start with the template:
```bash
dotnet new install Sekiban.Pure.Templates
dotnet new sekiban-orleans-aspire -n MyProject
```

### 2. API Configuration

The template generates a Program.cs with all necessary configurations. Here's how it works:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Aspire and Orleans integration
builder.AddServiceDefaults();
builder.UseOrleans(config =>
{
    config.UseDashboard(options => { });
    config.AddMemoryStreams("EventStreamProvider")
          .AddMemoryGrainStorage("EventStreamProvider");
});

// Register domain types and serialization
builder.Services.AddSingleton(
    OrleansSekibanDomainDomainTypes.Generate(
        OrleansSekibanDomainEventsJsonContext.Default.Options));

// Configure database (Cosmos DB or PostgreSQL)
if (builder.Configuration.GetSection("Sekiban").GetValue<string>("Database")?.ToLower() == "cosmos")
{
    builder.AddSekibanCosmosDb();
} else
{
    builder.AddSekibanPostgresDb();
}
```

### 3. API Endpoints

Map endpoints for commands and queries:

```csharp
// Query endpoint
apiRoute.MapGet("/weatherforecast", 
    async ([FromServices]SekibanOrleansExecutor executor) =>
    {
        var list = await executor.QueryAsync(new WeatherForecastQuery(""))
                                .UnwrapBox();
        return list.Items;
    })
    .WithOpenApi();

// Command endpoint
apiRoute.MapPost("/inputweatherforecast",
    async (
        [FromBody] InputWeatherForecastCommand command,
        [FromServices] SekibanOrleansExecutor executor) => 
            await executor.CommandAsync(command).ToSimpleCommandResponse().UnwrapBox())
    .WithOpenApi();
```

Key points:
- Use `SekibanOrleansExecutor` for handling commands and queries
- Commands are mapped to POST endpoints
- Queries are typically mapped to GET endpoints
- Results are unwrapped from `ResultBox` using `UnwrapBox()`
- OpenAPI support is included by default
- Command execution results can be simplified using `ToSimpleCommandResponse()`, making it easy to access important information like LastSortableUniqueId

### 4. Implementation Steps

1. Start with the project template
2. Define your domain model (aggregates)
3. Create commands that represent user intentions
4. Define events that represent state changes
5. Implement projectors to apply events to aggregates
6. Create queries to retrieve and filter data
7. Set up the JSON serialization context
8. Map your API endpoints using the `SekibanOrleansExecutor`

### 5. Configuration Options

The template supports two database options:
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

## Workflows and Domain Services

Sekiban supports implementing domain workflows and services that encapsulate business logic that spans multiple aggregates or requires specialized processing.

### Domain Workflows

Domain workflows are stateless services that implement business processes that may involve multiple aggregates or complex validation logic. They are particularly useful for:

1. **Cross-Aggregate Operations**: When a business process spans multiple aggregates
2. **Complex Validation**: When validation requires checking against multiple aggregates or external systems
3. **Reusable Business Logic**: When the same logic is used in multiple places

```csharp
// Example of a domain workflow for duplicate checking
namespace YourProject.Domain.Workflows;

public static class DuplicateCheckWorkflows
{
    // Result type for duplicate check operations
    public class DuplicateCheckResult
    {
        public bool IsDuplicate { get; }
        public string? ErrorMessage { get; }
        public object? CommandResult { get; }

        private DuplicateCheckResult(bool isDuplicate, string? errorMessage, object? commandResult)
        {
            IsDuplicate = isDuplicate;
            ErrorMessage = errorMessage;
            CommandResult = commandResult;
        }

        public static DuplicateCheckResult Duplicate(string errorMessage) => 
            new(true, errorMessage, null);

        public static DuplicateCheckResult Success(object commandResult) => 
            new(false, null, commandResult);
    }

    // Workflow method that checks for duplicate IDs before registering
    public static async Task<DuplicateCheckResult> CheckUserIdDuplicate(
        RegisterUserCommand command,
        ISekibanExecutor executor)
    {
        // Check if userId already exists
        var userIdExists = await executor.QueryAsync(new UserIdExistsQuery(command.UserId)).UnwrapBox();
        if (userIdExists)
        {
            return DuplicateCheckResult.Duplicate($"User with ID '{command.UserId}' already exists");
        }
        
        // If no duplicate, proceed with the command
        var result = await executor.CommandAsync(command).UnwrapBox();
        return DuplicateCheckResult.Success(result);
    }
}
```

**Key Points**:
- Workflows can be implemented as either static classes with static methods or instance-based classes with dependency injection 🏗️
- They should be placed in a `Workflows` folder or namespace 📁
- They should use `ISekibanExecutor` interface for better testability 🧪
- They should return domain-specific result types that encapsulate success/failure information 📊
- They can be called from API endpoints or other services 🔗

### Using Workflows in API Endpoints

```csharp
// In Program.cs
apiRoute.MapPost("/users/register",
    async ([FromBody] RegisterUserCommand command, [FromServices] SekibanOrleansExecutor executor) => 
    {
        // Use the workflow to check for duplicates
        var result = await DuplicateCheckWorkflows.CheckUserIdDuplicate(command, executor);
        if (result.IsDuplicate)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Duplicate UserId",
                detail: result.ErrorMessage);
        }
        return result.CommandResult;
    });
```

### Testing Workflows

Workflows can be tested using the same in-memory testing approach as other Sekiban components:

```csharp
public class DuplicateCheckWorkflowsTests : SekibanInMemoryTestBase
{
    protected override SekibanDomainTypes GetDomainTypes() => 
        YourDomainDomainTypes.Generate(YourDomainEventsJsonContext.Default.Options);

    [Fact]
    public async Task CheckUserIdDuplicate_WhenUserIdExists_ReturnsDuplicate()
    {
        // Arrange - Create a user with the ID we want to test
        var existingUserId = "U12345";
        var command = new RegisterUserCommand(
            "John Doe",
            existingUserId,
            "john@example.com");

        // Register a user with the same ID to ensure it exists
        GivenCommand(command);

        // Act - Try to register another user with the same ID
        var result = await DuplicateCheckWorkflows.CheckUserIdDuplicate(command, Executor);

        // Assert
        Assert.True(result.IsDuplicate);
        Assert.Contains(existingUserId, result.ErrorMessage);
        Assert.Null(result.CommandResult);
    }

    [Fact]
    public async Task CheckUserIdDuplicate_WhenUserIdDoesNotExist_ReturnsSuccess()
    {
        // Arrange
        var newUserId = "U67890";
        var command = new RegisterUserCommand(
            "Jane Doe",
            newUserId,
            "jane@example.com");

        // Act
        var result = await DuplicateCheckWorkflows.CheckUserIdDuplicate(command, Executor);

        // Assert
        Assert.False(result.IsDuplicate);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.CommandResult);
    }
}
```

**Key Points**:
- Use `SekibanInMemoryTestBase` for testing workflows
- The base class provides an `Executor` property that implements `ISekibanExecutor`
- Use `GivenCommand` to set up the test state
- Test both success and failure scenarios

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

5. **ISekibanExecutor vs. SekibanOrleansExecutor**: When implementing domain services or workflows, use `ISekibanExecutor` interface instead of the concrete `SekibanOrleansExecutor` class for better testability. The `ISekibanExecutor` interface is in the `Sekiban.Pure.Executors` namespace.

## Advanced Query Features

### Waiting for Specific Events

In event-sourced systems, there can be a gap between when a command is processed and when a query reflects the results of that command, especially in distributed systems. Sekiban provides a mechanism to ensure queries wait for specific events to be processed before returning results.

#### The IWaitForSortableUniqueId Interface

```csharp
public interface IWaitForSortableUniqueId
{
    string? WaitForSortableUniqueId { get; }
}
```

This interface allows query objects to specify that they should wait for a particular event (identified by its sortable unique ID) before executing.

#### Implementing Waiting Queries

To implement a query that can wait for specific events:

```csharp
[GenerateSerializer]
public record WeatherForecastQuery(string QueryId) : 
    IMultiProjectionListQuery<AggregateListProjector<WeatherForecastProjector>, 
                             WeatherForecastQuery, 
                             WeatherForecastQuery.WeatherForecastRecord>,
    IWaitForSortableUniqueId
{
    public string? WaitForSortableUniqueId { get; set; }

    // Query handling implementation...
    public static ResultBox<IEnumerable<WeatherForecastRecord>> HandleFilter(
        MultiProjectionState<AggregateListProjector<WeatherForecastProjector>> state,
        WeatherForecastQuery query,
        IQueryContext context)
    {
        // Implementation details...
    }

    // Result type
    [GenerateSerializer]
    public record WeatherForecastRecord(
        string Id,
        string Location,
        DateOnly Date,
        int TemperatureC,
        string Summary);
}
```

#### Using the Wait for Feature in API Clients

When implementing an API client:

```csharp
public async Task<WeatherForecastRecord[]> GetWeatherAsync(
    int maxItems = 10, 
    string? waitForSortableUniqueId = null, 
    CancellationToken cancellationToken = default)
{
    var requestUri = string.IsNullOrEmpty(waitForSortableUniqueId)
        ? "/api/weatherforecast"
        : $"/api/weatherforecast?waitForSortableUniqueId={Uri.EscapeDataString(waitForSortableUniqueId)}";
        
    // Make the HTTP request...
}
```

#### API Configuration

Configure your API endpoints to accept and use the wait parameter:

```csharp
apiRoute
    .MapGet(
        "/weatherforecast",
        async ([FromQuery] string? waitForSortableUniqueId, [FromServices] SekibanOrleansExecutor executor) =>
        {
            var query = new WeatherForecastQuery("")
            {
                WaitForSortableUniqueId = waitForSortableUniqueId
            };
            var list = await executor.QueryAsync(query).UnwrapBox();
            return list.Items;
        });
```

#### Client-Side Implementation for Immediate Updates

After executing a command, use the command's `LastSortableUniqueId` property when making subsequent queries:

```csharp
// Execute a command
var response = await WeatherApi.UpdateLocationAsync(
    weatherForecastId,
    newLocation);
    
// Query using the LastSortableUniqueId to ensure we get the updated state
var forecasts = await WeatherApi.GetWeatherAsync(
    waitForSortableUniqueId: response.LastSortableUniqueId);
```

This pattern ensures that your UI always reflects the most recent state changes, providing a more consistent user experience.

## Conclusion
