# Getting Started - Sekiban Event Sourcing

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md) (You are here)
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
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Object](15_value_object.md)
> - [Deployment Guide](16_deployment.md)

## Installation and Setup

Sekiban provides two implementation approaches:

### Orleans Version (Recommended)

```bash
# Install the Sekiban templates
dotnet new install Sekiban.Pure.Templates

# Create a new Orleans project
dotnet new sekiban-orleans-aspire -n MyProject
```

This template includes Aspire host for Orleans, Cluster Storage, Grain Persistent Storage, and Queue Storage.

### Dapr Version

```bash
# Install the Sekiban templates
dotnet new install Sekiban.Pure.Templates

# Create a new Dapr project
dotnet new sekiban-dapr-aspire -n MyProject
```

This template includes Aspire host for Dapr, Dapr actors, state store, and pub/sub capabilities.

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

To launch the AppHost with HTTPS profile, use:

```bash
dotnet run --project MyProject.AppHost --launch-profile https
```

## File Structure

The latest templates use a more structured folder hierarchy:

```
YourProject.Domain/
├── Aggregates/                         // Aggregate-related folder
│   └── YourEntity/                     // Entity-specific folder
│       ├── Commands/                   // Commands
│       │   ├── CreateYourEntityCommand.cs
│       │   ├── UpdateYourEntityCommand.cs
│       │   └── DeleteYourEntityCommand.cs
│       ├── Events/                     // Events
│       │   ├── YourEntityCreated.cs
│       │   ├── YourEntityUpdated.cs
│       │   └── YourEntityDeleted.cs
│       ├── Payloads/                   // Aggregate payloads
│       │   └── YourEntity.cs
│       ├── Queries/                    // Queries
│       │   └── YourEntityQuery.cs
│       └── YourEntityProjector.cs      // Projector
├── Projections/                        // Multi-projections
│   └── CustomProjection/
│       ├── YourCustomProjection.cs
│       └── YourCustomQuery.cs
├── ValueObjects/                       // Value objects
│   └── YourValueObject.cs
└── YourDomainEventsJsonContext.cs      // JSON Context
```

This structure helps organize related code more logically, following Domain-Driven Design principles.

## Initial Steps

1. **Define your domain model**: Start by identifying the key entities in your domain
2. **Create aggregates**: Implement aggregate payloads for each entity
3. **Define commands**: Create commands that represent user intentions
4. **Define events**: Create events that record state changes
5. **Implement projectors**: Create projectors that build current state from events
6. **Add queries**: Add query types to retrieve data
7. **Configure serialization**: Set up JSON serialization for your domain types
8. **Add API endpoints**: Create API endpoints for your commands and queries

## Example: Creating a Minimal Domain

Let's create a minimal Todo application domain:

1. Create a TodoItem aggregate payload:
   ```csharp
   using Orleans.Serialization.Attributes;
   using Sekiban.Pure.Aggregates;

   [GenerateSerializer]
   public record TodoItem(string Title, bool IsCompleted = false) : IAggregatePayload;
   ```

2. Create TodoItem events:
   ```csharp
   using Orleans.Serialization.Attributes;
   using Sekiban.Pure.Events;

   [GenerateSerializer]
   public record TodoItemCreated(string Title) : IEventPayload;
   
   [GenerateSerializer]
   public record TodoItemCompleted : IEventPayload;
   ```

3. Create TodoItem commands:
   ```csharp
   using Orleans.Serialization.Attributes;
   using Sekiban.Pure.Aggregates;
   using Sekiban.Pure.Command.Handlers;
   using Sekiban.Pure.Documents;
   using Sekiban.Pure.Events;
   using Sekiban.Pure.ResultBoxes;

   [GenerateSerializer]
   public record CreateTodoItem(string Title) 
       : ICommandWithHandler<CreateTodoItem, TodoItemProjector>
   {
       public PartitionKeys SpecifyPartitionKeys(CreateTodoItem command) => 
           PartitionKeys.Generate<TodoItemProjector>();
           
       public ResultBox<EventOrNone> Handle(CreateTodoItem command, ICommandContext<IAggregatePayload> context)
           => EventOrNone.Event(new TodoItemCreated(command.Title));
   }
   ```

4. Create TodoItem projector:
   ```csharp
   using Sekiban.Pure.Aggregates;
   using Sekiban.Pure.Events;
   using Sekiban.Pure.Projectors;

   public class TodoItemProjector : IAggregateProjector
   {
       public IAggregatePayload Project(IAggregatePayload payload, IEvent ev)
           => (payload, ev.GetPayload()) switch
           {
               (EmptyAggregatePayload, TodoItemCreated e) => new TodoItem(e.Title),
               (TodoItem item, TodoItemCompleted _) => item with { IsCompleted = true },
               _ => payload
           };
   }
   ```

Next, follow the other guides to implement queries, API endpoints, and more.