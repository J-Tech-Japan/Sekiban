# Aggregate Payload, Projector, Command and Events - Sekiban Event Sourcing

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Aggregate, Projector, Command and Events](03_aggregate_command_events.md) (You are here)
> - [Multiple Aggregate Projector](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Workflow](06_workflow.md)
> - [JSON and Orleans Serialization](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md)
> - [Client API (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md)
> - [Unit Testing](11_unit_testing.md)
> - [Common Issues and Solutions](12_common_issues.md)

## 1. Aggregate Payload (Domain Entity)

An aggregate is a domain entity that encapsulates state and business rules. In Sekiban, aggregates are implemented as immutable records:

```csharp
using Orleans.Serialization.Attributes;
using Sekiban.Pure.Aggregates;

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

### Example: User Aggregate

```csharp
using Orleans.Serialization.Attributes;
using Sekiban.Pure.Aggregates;

[GenerateSerializer]
public record User(string Name, string Email, bool IsConfirmed = false) : IAggregatePayload
{
    // Domain logic methods
    public User WithConfirmation() => this with { IsConfirmed = true };
    
    public User UpdateEmail(string newEmail) => this with { Email = newEmail };
}
```

## 2. Commands (User Intentions)

Commands represent user intentions to change system state. They are implemented as records with handlers:

```csharp
using Orleans.Serialization.Attributes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.ResultBoxes;

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

### Example: Create User Command

```csharp
using Orleans.Serialization.Attributes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.ResultBoxes;

[GenerateSerializer]
public record CreateUser(string Name, string Email) 
    : ICommandWithHandler<CreateUser, UserProjector>
{
    public PartitionKeys SpecifyPartitionKeys(CreateUser command) => 
        PartitionKeys.Generate<UserProjector>();
        
    public ResultBox<EventOrNone> Handle(CreateUser command, ICommandContext<IAggregatePayload> context)
        => EventOrNone.Event(new UserCreated(command.Name, command.Email));
}
```

### Using the Third Generic Parameter for State Constraints

You can specify a third generic parameter to enforce state-based constraints at the type level:

```csharp
using Orleans.Serialization.Attributes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.ResultBoxes;
using System;

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

### Accessing Aggregate Payload in Commands

There are two ways to access the aggregate payload in command handlers, depending on whether you use the two or three generic parameter version:

1. **With Type Constraint (Three Generic Parameters)**:
   ```csharp
   using Sekiban.Pure.Aggregates;
   using Sekiban.Pure.Command.Handlers;
   using Sekiban.Pure.Events;
   using Sekiban.Pure.ResultBoxes;

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
   using Sekiban.Pure.Aggregates;
   using Sekiban.Pure.Command.Handlers;
   using Sekiban.Pure.Events;
   using Sekiban.Pure.ResultBoxes;

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

### Generating Multiple Events from a Command

If a command needs to generate multiple events, you can use the `AppendEvent` method on the command context:

```csharp
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Events;
using Sekiban.Pure.ResultBoxes;

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

## 3. Events (Facts That Happened)

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
   using Orleans.Serialization.Attributes;
   using Sekiban.Pure.Events;

   [GenerateSerializer]
   public record YourEvent(...parameters...) : IEventPayload;
   ```

**Required**:
- Implement `IEventPayload` interface for domain-specific data only
- Use past tense naming (Created, Updated, Deleted)
- Add `[GenerateSerializer]` attribute
- Include all data needed to reconstruct domain state

### Example: User Events

```csharp
using Orleans.Serialization.Attributes;
using Sekiban.Pure.Events;

[GenerateSerializer]
public record UserCreated(string Name, string Email) : IEventPayload;

[GenerateSerializer]
public record UserConfirmed : IEventPayload;

[GenerateSerializer]
public record UserUnconfirmed : IEventPayload;

[GenerateSerializer]
public record EmailChanged(string NewEmail) : IEventPayload;
```

## PartitionKeys Structure

```csharp
using Sekiban.Pure.Documents;

public class PartitionKeys
{
    string RootPartitionKey;  // Optional tenant key for multi-tenancy
    string AggregateGroup;    // Usually projector name
    Guid AggregateId;        // Unique identifier
}
```

**Usage in Commands**:
```csharp
using Sekiban.Pure.Documents;

// For new aggregates:
public PartitionKeys SpecifyPartitionKeys(YourCommand command) => 
    PartitionKeys.Generate<YourProjector>();

// For existing aggregates:
public PartitionKeys SpecifyPartitionKeys(YourCommand command) => 
    PartitionKeys.Existing<YourProjector>(command.AggregateId);
```

## 4. Projector (State Builder)

Projectors build the current state by applying events to aggregates:

```csharp
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;

// Example of state transitions in projector
public class UserProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev)
        => (payload, ev.GetPayload()) switch
        {
            // Each case can return a different payload type to represent state
            (EmptyAggregatePayload, UserCreated e) => new UnconfirmedUser(e.Name, e.Email),
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

### Example: Todo Item Projector

```csharp
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;

public class TodoItemProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev)
        => (payload, ev.GetPayload()) switch
        {
            // Initial state creation
            (EmptyAggregatePayload, TodoItemCreated e) => new TodoItem(e.Title),
            
            // State updates
            (TodoItem item, TodoItemCompleted _) => item with { IsCompleted = true },
            (TodoItem item, TodoItemDescriptionChanged e) => item with { Description = e.Description },
            (TodoItem item, TodoItemDueDateSet e) => item with { DueDate = e.DueDate },
            
            // Default case: return unchanged payload
            _ => payload
        };
}
```

## Multiple Projectors for Single Aggregate

One powerful feature of Sekiban is the ability to use multiple projectors on the same event stream. As long as the PartitionKeys align, you can create different views of the same events using different projectors.

### Using LoadAggregateAsync for Multiple Projections

The `LoadAggregateAsync` method on `SekibanOrleansExecutor` allows you to load an aggregate using any projector that shares the same stream structure:

```csharp
using Sekiban.Pure.Executors;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.ResultBoxes;
using System.Threading.Tasks;
using System;

// First, load with the primary projector
var partitionKeys = PartitionKeys.Existing<UserProjector>(userId);
var result = await sekibanExecutor.LoadAggregateAsync<UserProjector>(partitionKeys);

// Then load the same events with a different projector
var userActivityResult = await sekibanExecutor.LoadAggregateAsync<UserActivityProjector>(partitionKeys);

// Both projections use the same events but produce different views
if (result.IsSuccess && userActivityResult.IsSuccess)
{
    var user = result.GetValue().GetPayload();  // Standard user projection
    var activity = userActivityResult.GetValue().GetPayload();  // Activity-focused projection
    
    // Work with both projections...
}
```

### Example: Multiple Projections for User Data

```csharp
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using System;
using System.Collections.Generic;

// Standard user projector
public class UserProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev)
        => (payload, ev.GetPayload()) switch
        {
            (EmptyAggregatePayload, UserCreated e) => new User(e.Name, e.Email),
            (User user, EmailChanged e) => user with { Email = e.NewEmail },
            _ => payload
        };
}

// Activity-focused projector for the same events
public class UserActivityProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev)
    {
        var activityLog = payload is UserActivity activity 
            ? activity.ActivityLog 
            : new List<UserActivityEntry>();
            
        // Add new activity based on event type
        switch (ev.GetPayload())
        {
            case UserCreated:
                activityLog.Add(new UserActivityEntry(ev.Timestamp, "User Created"));
                break;
            case EmailChanged e:
                activityLog.Add(new UserActivityEntry(ev.Timestamp, $"Email changed to {e.NewEmail}"));
                break;
            case UserConfirmed:
                activityLog.Add(new UserActivityEntry(ev.Timestamp, "Account confirmed"));
                break;
        }
        
        return new UserActivity(activityLog);
    }
}

public record UserActivity(List<UserActivityEntry> ActivityLog) : IAggregatePayload;
public record UserActivityEntry(DateTime Timestamp, string Activity);
```

**Benefits of Multiple Projectors**:

1. **Different perspectives**: View the same event stream from different angles
2. **Specialized projections**: Create task-specific views of the same data
3. **Separation of concerns**: Keep your primary aggregate model clean while adding specialized views
4. **Evolving models**: Add new projectors without changing existing ones
```