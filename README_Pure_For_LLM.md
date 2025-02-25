# Sekiban Event Sourcing - LLM Implementation Guide

## Getting Started

```bash
# Install the Sekiban templates
dotnet new install Sekiban.Pure.Templates

# Create a new project
dotnet new sekiban-orleans-aspire -n MyProject
```

This template includes Aspire host for Orleans, Cluster Storage, Grain Persistent Storage, and Queue Storage.

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
- Implement `ICommandWithHandler<TCommand, TProjector>` interface
- Implement `SpecifyPartitionKeys` method:
  - For new aggregates: `PartitionKeys.Generate<YourProjector>()`
  - For existing aggregates: `PartitionKeys.Existing<YourProjector>(aggregateId)`
- Implement `Handle` method that returns events
- Add `[GenerateSerializer]` attribute

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
- Return different payload types to enforce business rules:
  ```csharp
  // Command handler can check state type
  if (context.AggregatePayload is ConfirmedUser user)
  {
      // Only confirmed users can perform this action
  }
  ```
- Handle initial state creation from `EmptyAggregatePayload`
- Maintain immutability in state changes

### 5. Query (Data Retrieval)

```csharp
[GenerateSerializer]
public record YourQuery(string FilterParameter)
    : IMultiProjectionListQuery<AggregateListProjector<YourAggregateProjector>, YourQuery, YourQuery.ResultRecord>
{
    public static ResultBox<IEnumerable<ResultRecord>> HandleFilter(
        MultiProjectionState<AggregateListProjector<YourAggregateProjector>> projection, 
        YourQuery query, 
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
        YourQuery query, 
        IQueryContext context)
    {
        return filteredList.OrderBy(m => m.SomeProperty).AsEnumerable().ToResultBox();
    }

    [GenerateSerializer]
    public record ResultRecord(Guid Id, ...other properties...);
}
```

**Required**:
- Implement appropriate query interface (e.g., `IMultiProjectionListQuery`)
- Implement static `HandleFilter` method
- Implement static `HandleSort` method
- Define nested result record with `[GenerateSerializer]` attribute

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
    config.AddMemoryStreams("EventStreamProvider")
          .AddMemoryGrainStorage("EventStreamProvider");
});

// 2. Register Domain
builder.Services.AddSingleton(
    OrleansSekibanDomainDomainTypes.Generate(
        OrleansSekibanDomainEventsJsonContext.Default.Options));

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
        var result = await executor.QueryAsync(new YourQuery(""))
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
