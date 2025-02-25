# Sekiban Event Sourcing Guide for LLM Programming Agents

[日本語版はこちら (Japanese Version)](README_Pure_JP.md)

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
- Implement `ICommandWithHandler<TCommand, TProjector>` interface
- Include the `[GenerateSerializer]` attribute
- Define `SpecifyPartitionKeys` method to determine where the aggregate is stored:
  - For new aggregates: `PartitionKeys.Generate<YourProjector>()`
  - For existing aggregates: `PartitionKeys.Existing<YourProjector>(aggregateId)`
- Implement `Handle` method that returns events or no events
- Commands don't modify state directly; they produce events

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

6. **Error Handling**:
   - Use `ResultBox` to handle errors and return meaningful messages
   - Validate commands before producing events
   - Handle edge cases in projectors

## Creating a New Sekiban Project

1. Start with the appropriate project template
2. Define your domain model (aggregates)
3. Create commands that represent user intentions
4. Define events that represent state changes
5. Implement projectors to apply events to aggregates
6. Create queries to retrieve and filter data
7. Set up the JSON serialization context

## Conclusion

Sekiban provides a powerful framework for implementing event sourcing in .NET applications. By understanding the key components and following the best practices outlined in this guide, LLM programming agents can effectively create and maintain Sekiban event sourcing projects.
