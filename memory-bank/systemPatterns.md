# System Patterns: Sekiban

## System Architecture

Sekiban implements a clean, modular architecture based on Event Sourcing and CQRS principles. The system is organized into several key layers:

### 1. Domain Layer
- Contains the core domain model
- Defines aggregates, events, and commands
- Implements business logic and validation rules
- Pure domain logic with no infrastructure dependencies

### 2. Application Layer
- Handles command and query execution
- Coordinates between domain and infrastructure
- Manages transaction boundaries
- Implements application services

### 3. Infrastructure Layer
- Provides concrete implementations for persistence
- Manages event storage and retrieval
- Implements projection storage
- Handles serialization and deserialization

### 4. API Layer
- Exposes HTTP endpoints for commands and queries
- Handles request/response mapping
- Provides OpenAPI/Swagger documentation
- Manages authentication and authorization

## Key Technical Decisions

### 1. Immutable Data Structures
Sekiban uses C# records for immutable data structures, ensuring that domain objects cannot be modified after creation. This aligns with event sourcing principles where state changes are represented as new events rather than mutations.

### 2. Type-Safe API
The framework leverages C#'s strong typing to provide compile-time safety. Generic interfaces ensure that commands, events, and projections are correctly implemented and connected.

### 3. Storage Abstraction
Sekiban abstracts storage details behind interfaces, allowing different backends (Cosmos DB, DynamoDB, PostgreSQL) to be used interchangeably without changing domain code.

### 4. Orleans Integration
The newer Sekiban.Pure version integrates with Microsoft Orleans, leveraging its virtual actor model for distributed processing and state management.

### 5. JSON Serialization
Sekiban uses System.Text.Json with source generation for efficient serialization and deserialization, supporting both runtime and AOT compilation scenarios.

## Design Patterns

### 1. Command Pattern
Commands encapsulate user intentions and are processed by command handlers that validate input and produce events.

```csharp
[GenerateSerializer]
public record CreateUserCommand(string Name, string Email) 
    : ICommandWithHandler<CreateUserCommand, UserProjector>
{
    public PartitionKeys SpecifyPartitionKeys(CreateUserCommand command) => 
        PartitionKeys.Generate<UserProjector>();
        
    public ResultBox<EventOrNone> Handle(CreateUserCommand command, ICommandContext<IAggregatePayload> context)
        => EventOrNone.Event(new UserCreated(command.Name, command.Email));
}
```

### 2. Event Sourcing Pattern
All state changes are stored as a sequence of events, which are the source of truth for the system.

```csharp
[GenerateSerializer]
public record UserCreated(string Name, string Email) : IEventPayload;
```

### 3. Projection Pattern
Events are applied to aggregates to build the current state through projectors.

```csharp
public class UserProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev)
        => (payload, ev.GetPayload()) switch
        {
            (EmptyAggregatePayload, UserCreated e) => new User(e.Name, e.Email),
            (User user, UserEmailUpdated e) => user with { Email = e.Email },
            _ => payload
        };
}
```

### 4. Query Pattern
Queries retrieve and filter data from projections.

```csharp
[GenerateSerializer]
public record UserListQuery(string NameFilter = null)
    : IMultiProjectionListQuery<AggregateListProjector<UserProjector>, UserListQuery, UserListQuery.UserRecord>
{
    public static ResultBox<IEnumerable<UserRecord>> HandleFilter(
        MultiProjectionState<AggregateListProjector<UserProjector>> projection, 
        UserListQuery query, 
        IQueryContext context)
    {
        // Filter implementation
    }

    public static ResultBox<IEnumerable<UserRecord>> HandleSort(
        IEnumerable<UserRecord> filteredList, 
        UserListQuery query, 
        IQueryContext context)
    {
        // Sort implementation
    }

    [GenerateSerializer]
    public record UserRecord(Guid Id, string Name, string Email);
}
```

### 5. State Machine Pattern
Aggregate state transitions are modeled as different payload types, enabling type-safe state-dependent operations.

```csharp
// Different states as different payload types
public record UnconfirmedUser(string Name, string Email) : IAggregatePayload;
public record ConfirmedUser(string Name, string Email) : IAggregatePayload;

// State-specific command
public record ConfirmUser(Guid UserId) 
    : ICommandWithHandler<ConfirmUser, UserProjector, UnconfirmedUser>
{
    // This command can only be executed on UnconfirmedUser state
}
```

## Component Relationships

### Command → Event Relationship
Commands validate input and produce events. A command can produce zero, one, or multiple events.

```
Command → Validation → Event(s)
```

### Event → Aggregate Relationship
Events are applied to aggregates by projectors to build the current state.

```
Event → Projector → Aggregate State
```

### Aggregate → Command Relationship
Commands can access the current aggregate state for validation and decision-making.

```
Aggregate State → Command → New Event(s)
```

### Query → Projection Relationship
Queries read from projections to retrieve and filter data.

```
Query → Projection → Result
```

### Partition Key Structure
Partition keys organize data in the storage backend and consist of:
- RootPartitionKey (optional tenant key)
- AggregateGroup (usually projector name)
- AggregateId (unique identifier)

This structure enables efficient data access patterns and supports multi-tenancy.

## Data Flow

1. **Command Flow**
   ```
   Client → API → Command → Command Handler → Event(s) → Event Store → Projector → Updated State
   ```

2. **Query Flow**
   ```
   Client → API → Query → Query Handler → Projection → Filtered/Sorted Results → Client
   ```

3. **Event Replay Flow**
   ```
   Event Store → Events → Projector → Aggregate State
   ```

4. **Snapshot Flow**
   ```
   Aggregate State → Snapshot → Storage
   Snapshot → Aggregate State (fast recovery)
   ```

This architecture ensures a clean separation of concerns, with each component having a single responsibility and clear relationships with other components.
