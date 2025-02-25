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

```csharp
[GenerateSerializer]
public record YourEvent(...parameters...) : IEventPayload;
```

**Required**:
- Implement `IEventPayload` interface
- Use past tense naming (Created, Updated, Deleted)
- Add `[GenerateSerializer]` attribute
- Include all data needed for state reconstruction

### 4. Projector (State Builder)

```csharp
public class YourAggregateProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev)
        => (payload, ev.GetPayload()) switch
        {
            (EmptyAggregatePayload, YourCreatedEvent created) => new YourAggregate(...),
            (YourAggregate aggregate, YourUpdatedEvent updated) => aggregate with { Property = updated.NewValue },
            (YourAggregate aggregate, YourDeletedEvent _) => new DeletedYourAggregate(...),
            _ => payload
        };
}
```

**Required**:
- Implement `IAggregateProjector` interface
- Implement `Project` method using pattern matching
- Handle initial state creation from `EmptyAggregatePayload`
- Return new state for each event type

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

## Implementation Steps

1. Define aggregate (domain entity) implementing `IAggregatePayload`
2. Create events implementing `IEventPayload`
3. Implement projector with `IAggregateProjector` interface
4. Create commands implementing `ICommandWithHandler<TCommand, TProjector>`
5. Define queries implementing appropriate query interface
6. Set up JSON serialization context

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
