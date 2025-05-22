# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Sekiban** is an Event Sourcing and CQRS framework for .NET 9 built with Microsoft Orleans. It supports Azure Cosmos DB, PostgreSQL, and in-memory storage for event stores. The main repository focuses on **Sekiban.Pure.Orleans** (the new version), while the legacy **Sekiban.Core** is on the `core_main` branch.

## Build and Test Commands

### Core Commands
```bash
# Build entire solution
dotnet build Sekiban.sln

# Restore dependencies (includes benchmark source)
dotnet nuget add source -n benchmarknightly https://www.myget.org/F/benchmarkdotnet/api/v3/index.json
dotnet restore Sekiban.sln

# Build for release
dotnet build Sekiban.sln -c Release
```

### Testing Commands
```bash
# Run all test projects
dotnet test tests/MemStat.Net.Test/MemStat.Net.Test.csproj
dotnet test tests/Pure.Domain.Test/Pure.Domain.Test.csproj
dotnet test tests/Pure.Domain.xUnit/Pure.Domain.xUnit.csproj

# Run tests with specific configuration
dotnet test -c Release --no-build --verbosity normal

# Run Playwright UI tests
cd Samples/Tutorials/OrleansSekiban/OrleansSekiban.Playwright
dotnet test

# Install Playwright browsers (if needed)
./install-browsers.sh
```

### Development Setup
```bash
# Create new project from template
dotnet new install Sekiban.Pure.Templates
dotnet new sekiban-orleans-aspire -n MyProject

# Run Orleans/Aspire samples
dotnet run --project Samples/Tutorials/OrleansSekiban/OrleansSekiban.AppHost
dotnet run --project Samples/Tutorials/AspireEventSample/AspireEventSample.AppHost
```

## Architecture Overview

### Core Framework Structure
- **Sekiban.Pure** - Core event sourcing abstractions and in-memory implementation
- **Sekiban.Pure.Orleans** - Orleans grain-based distributed implementation with actor model
- **Sekiban.Pure.SourceGenerator** - Automatic domain type registration code generation
- **Storage Providers**: CosmosDb, Postgres, ReadModel support
- **Testing Libraries**: xUnit, NUnit integration with in-memory and Orleans test bases

### Key Architectural Patterns

**Event Sourcing Flow:**
1. Commands → Events (via Command Handlers)
2. Events → Aggregate State (via Projectors)  
3. Aggregate State → Queries (via Query Handlers)
4. Cross-aggregate data via MultiProjections

**Orleans Integration:**
- Aggregates are Orleans grains for scalability
- Event streams partitioned by `PartitionKeys` (AggregateId, Group, RootPartitionKey)
- Automatic grain persistence and state management
- Built-in multi-tenancy via RootPartitionKey

**Source Generation:**
- Domain types automatically registered at build time
- Generated classes follow pattern: `[ProjectName]DomainDomainTypes`
- Placed in `[ProjectName].Generated` namespace

## Development Conventions

### Naming Standards
- **Commands**: Imperative verbs (`CreateUser`, `UpdateProfile`)
- **Events**: Past tense (`UserCreated`, `ProfileUpdated`)  
- **Aggregates**: Domain nouns (`User`, `Order`)
- **Projectors**: Aggregate name + "Projector" (`UserProjector`)

### Required Attributes
All domain types must include `[GenerateSerializer]` for Orleans serialization:
```csharp
[GenerateSerializer]
public record CreateUserCommand(...) : ICommandWithHandler<CreateUserCommand, UserProjector>

[GenerateSerializer] 
public record UserCreated(...) : IEventPayload

[GenerateSerializer]
public record User(...) : IAggregatePayload
```

### File Organization Pattern
```
Domain/
├── Aggregates/
│   └── User/
│       ├── Commands/
│       ├── Events/ 
│       ├── Payloads/
│       ├── Queries/
│       └── UserProjector.cs
├── Projections/           # Cross-aggregate projections
├── ValueObjects/
└── [Domain]EventsJsonContext.cs
```

## Testing Strategy

### Test Base Classes
- `SekibanInMemoryTestBase` - Fast in-memory testing
- `SekibanOrleansTestBase<T>` - Full Orleans integration testing
- Manual setup with `InMemorySekibanExecutor` for custom scenarios

### Test Methods Pattern
```csharp
// Simple assertion style
var response = GivenCommand(new CreateUser(...));
var result = WhenCommand(new UpdateUser(...));
var aggregate = ThenGetAggregate<UserProjector>(...);

// Fluent chaining style with ResultBox
GivenCommandWithResult(new CreateUser(...))
    .Conveyor(r => WhenCommandWithResult(new UpdateUser(...)))
    .Do(Assert.NotNull)
    .UnwrapBox();
```

## Configuration Requirements

### appsettings.json Structure
```json
{
  "Sekiban": {
    "Database": "Cosmos",  // or "Postgres"
    "CosmosDb": {
      "ConnectionString": "...",
      "DatabaseId": "...",
      "ContainerId": "..."
    },
    "Postgres": {
      "ConnectionString": "..."
    }
  }
}
```

### Program.cs Setup Pattern
```csharp
// 1. Configure Orleans
builder.UseOrleans(config => { ... });

// 2. Register generated domain types
builder.Services.AddSingleton(
    YourDomainDomainTypes.Generate(YourEventsJsonContext.Default.Options));

// 3. Configure storage
builder.AddSekibanCosmosDb(); // or AddSekibanPostgresDb()

// 4. Map command/query endpoints
app.MapPost("/api/command", async ([FromBody] Command cmd, SekibanOrleansExecutor exec) => 
    await exec.CommandAsync(cmd).ToSimpleCommandResponse().UnwrapBox());
```

## Special Implementation Notes

### PartitionKeys Management
- `PartitionKeys.Generate<TProjector>()` for new aggregates
- `PartitionKeys.Existing<TProjector>(id)` for existing aggregates
- `PartitionKeys.Generate<TProjector>("tenant")` for multi-tenant scenarios

### State Machine Pattern
Use different payload types to represent aggregate states:
```csharp
// States as separate payload types
public record UnconfirmedUser(...) : IAggregatePayload;
public record ConfirmedUser(...) : IAggregatePayload;

// Commands can enforce state constraints
public record ConfirmUser(...) : ICommandWithHandler<ConfirmUser, UserProjector, UnconfirmedUser>
```

### ResultBox Pattern  
Sekiban uses `ResultBox<T>` for error handling instead of exceptions:
- `.UnwrapBox()` - throws on failure
- `.Conveyor()` - chains operations
- `.Do()` - side effects without changing result

### Query Consistency
Implement `IWaitForSortableUniqueId` on queries to wait for specific events:
```csharp
public record UserQuery(...) : IMultiProjectionQuery<...>, IWaitForSortableUniqueId
{
    public string? WaitForSortableUniqueId { get; set; }
}
```

## Important Development Guidelines

1. **Always use namespace `Sekiban.Pure.*`** - not `Sekiban.Core.*`
2. **Source generation requires successful build** before domain types are available
3. **Test serialization** for Orleans compatibility using `CheckSerializability()`
4. **Multi-tenancy** is built-in via RootPartitionKey in PartitionKeys
5. **Commands should be small and focused** - use workflows for complex business processes
6. **Events are immutable** - never modify existing event types, create new versions instead