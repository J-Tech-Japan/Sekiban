# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## ⚠️ CRITICAL RULE FOR TYPESCRIPT DEVELOPMENT

**NEVER CREATE SIMPLIFIED IMPLEMENTATIONS THAT SKIP PROPER ACTOR IMPLEMENTATION.**

The Sekiban TypeScript implementation is under development. When you encounter missing features:
- DO NOT create in-memory workarounds
- DO NOT bypass the actor system
- DO NOT create "temporary" solutions
- DO ask about the proper approach
- DO help implement features properly

Every simplified implementation creates garbage code that must be completely rewritten.

## Project Overview

**Sekiban** is an Event Sourcing and CQRS framework available for both .NET and TypeScript/JavaScript. This repository contains:

1. **Sekiban for .NET** - Built with Microsoft Orleans, supporting Azure Cosmos DB, PostgreSQL, and in-memory storage
2. **Sekiban for TypeScript** - Modern TypeScript implementation with Dapr support for distributed systems

### TypeScript Project Structure (ts/)

The TypeScript implementation is located under `/ts/` with the following structure:

```
ts/
├── src/packages/          # Core Sekiban packages
│   ├── core/             # Core event sourcing abstractions
│   ├── cosmos/           # Azure Cosmos DB storage provider
│   ├── postgres/         # PostgreSQL storage provider
│   ├── dapr/             # Dapr actors integration
│   ├── config/           # Configuration management
│   ├── migration/        # Event versioning support
│   ├── saga/             # Saga/process manager support
│   ├── testing/          # Testing utilities
│   └── codegen/          # Code generation tools
└── samples/              # Sample applications
    └── dapr-sample/      # Example using Dapr actors
```

## TypeScript Development Guide

### ⚠️ CRITICAL: Sekiban TypeScript is Under Development

**NEVER create simplified implementations that skip proper actor implementation.** This creates garbage code that must be rewritten.

When working with Sekiban TypeScript:
1. **If actors are not fully implemented yet** - Wait or help implement them properly
2. **Don't create "temporary" in-memory executors** - They will need complete rewriting
3. **Don't bypass the actor system** - The entire architecture depends on it
4. **If something is missing** - Ask before implementing a workaround

### Package Purpose and Relationships

#### 1. **@sekiban/core** - Foundation Package
- **Purpose**: Core event sourcing and CQRS abstractions
- **Key exports**: 
  - Events, Commands, Projectors, Queries
  - Schema registry with Zod validation
  - In-memory event store for testing
  - Domain types system
- **Use**: Always required, foundation for all other packages

#### 2. **Storage Providers** 
- **@sekiban/postgres**: PostgreSQL event store implementation
- **@sekiban/cosmos**: Azure Cosmos DB event store implementation
- **Purpose**: Persist events to respective databases
- **Use**: Choose one based on your database preference

#### 3. **@sekiban/dapr** - Distributed Systems
- **Purpose**: Adds distributed capabilities via Dapr actors
- **Key features**:
  - Aggregates become virtual actors
  - Built-in snapshot support
  - Pub/sub event distribution
- **Use**: For scalable, distributed applications
- **Important**: Requires Dapr runtime and proper actor registration

#### 4. **@sekiban/config** - Configuration
- **Purpose**: Runtime configuration and storage provider selection
- **Use**: For configurable applications that switch storage providers

### Creating a Sekiban Application

Follow the structure in the samples directory for reference. Key concepts:

1. **Domain Layer**: Define events, commands, and projectors using the schema-based approach
2. **Use createSchemaDomainTypes()**: Export domain types for use with executors
3. **Choose appropriate executor**: Use the right executor for your needs (Dapr for distributed, etc.)

### Important Notes for Dapr Integration

1. **Actors are NOT automatically created** - The Dapr actors are implemented in the @sekiban/dapr package but are NOT automatically registered
2. **Actor registration approach**: The current implementation expects actors to be hosted within the same application process that uses them
3. **Dapr runtime requirements**:
   - Dapr CLI installed and initialized
   - Placement service running
   - Proper components configured (state store, pubsub)

### ⚠️ About Actor Implementation

The Sekiban TypeScript actor system is still being developed. When you encounter missing functionality:

1. **DO NOT** create simplified workarounds (like in-memory executors)
2. **DO NOT** bypass the actor system with direct event store access
3. **DO** check the @sekiban/dapr package for the current implementation status
4. **DO** ask about the proper implementation approach
5. **DO** help implement missing features properly if needed

Every "simplified" implementation creates technical debt and confusion.

### Sample Application Structure

See the `ts/samples/` directory for working examples. Key structure:
- `domain/` - Events, Commands, Projectors  
- `api/` - REST API or other interfaces
- `dapr/` - Dapr configuration (if using Dapr)

### Common Mistakes to Avoid

1. **Don't create custom actor implementations** - Use the actors from @sekiban/dapr
2. **Don't mix storage providers** - Choose one (postgres, cosmos, or in-memory)
3. **Don't forget to register domain types** - All events, commands, projectors must be registered
4. **Don't assume Dapr actors are automatic** - They require proper setup and registration

### Build and Test Commands (TypeScript)

- `pnpm install` - Install dependencies
- `pnpm build` - Build all packages
- `pnpm test` - Run tests
- See samples directory for working examples

## .NET Development Guide

### Build and Test Commands

- `dotnet build Sekiban.sln` - Build solution
- `dotnet test` - Run all tests
- `dotnet run --project [sample-path]` - Run samples
- Templates available via `dotnet new sekiban-orleans-aspire`

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
All domain types must include `[GenerateSerializer]` for Orleans serialization in .NET.

### File Organization Pattern
Follow the domain-driven design structure with clear separation of:
- Aggregates (with their commands, events, and projectors)
- Cross-aggregate projections
- Value objects
- Domain type registration

## Testing Strategy

### Test Base Classes
- `SekibanInMemoryTestBase` - Fast in-memory testing
- `SekibanOrleansTestBase<T>` - Full Orleans integration testing
- Manual setup with `InMemorySekibanExecutor` for custom scenarios

### Test Methods Pattern
Use the provided test base classes for consistent testing patterns.

## Configuration Requirements

Configure Sekiban through appsettings.json with database connection strings and storage provider selection.

### Program.cs Setup Pattern
1. Configure Orleans
2. Register generated domain types
3. Configure storage provider
4. Map command/query endpoints

## Special Implementation Notes

### PartitionKeys Management
- `PartitionKeys.Generate<TProjector>()` for new aggregates
- `PartitionKeys.Existing<TProjector>(id)` for existing aggregates
- `PartitionKeys.Generate<TProjector>("tenant")` for multi-tenant scenarios

### State Machine Pattern
Use different payload types to represent aggregate states. Commands can enforce state constraints by specifying the required aggregate state type.

### ResultBox Pattern  
Sekiban uses `ResultBox<T>` for error handling instead of exceptions:
- `.UnwrapBox()` - throws on failure
- `.Conveyor()` - chains operations
- `.Do()` - side effects without changing result

### Query Consistency
Implement `IWaitForSortableUniqueId` on queries to wait for specific events before returning results.

## Important Development Guidelines

1. **Always use namespace `Sekiban.Pure.*`** - not `Sekiban.Core.*`
2. **Source generation requires successful build** before domain types are available
3. **Test serialization** for Orleans compatibility using `CheckSerializability()`
4. **Multi-tenancy** is built-in via RootPartitionKey in PartitionKeys
5. **Commands should be small and focused** - use workflows for complex business processes
6. **Events are immutable** - never modify existing event types, create new versions instead

## Development Principles

### General Principles
1. **NEVER make simplified solutions** - Always use the proper Sekiban implementation
2. **Understand package boundaries** - Each package has a specific purpose, don't mix responsibilities
3. **Follow the framework patterns** - Sekiban has specific patterns for events, commands, and projectors
4. **If something is missing** - Ask before implementing, don't create workarounds

### TypeScript Specific
1. **Always use schema-based events and commands** - Leverage Zod for runtime validation
2. **Register all domain types** - The registry is essential for the framework to work
3. **Choose one storage provider** - Don't try to use multiple providers in the same application
4. **Dapr actors are mandatory for distributed systems** - Don't bypass with simplified implementations
5. **The TypeScript version is under development** - Missing features should be implemented properly, not worked around

### When Creating Samples
1. **Use the actual Sekiban packages** - No simplified mock implementations
2. **If actors aren't ready** - Either wait or help implement them properly
3. **Document what's actually implemented** - Don't create examples for features that don't exist
4. **Test with the real system** - No shortcuts or mocks of core functionality