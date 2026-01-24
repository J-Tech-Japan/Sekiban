# Getting Started - Sekiban DCB

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md) (You are here)
> - [Commands, Events, Tags, Projectors](03_aggregate_command_events.md)
> - [MultiProjection](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Command Workflow](06_workflow.md)
> - [Serialization & Domain Types](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md)
> - [Client UI (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md)
> - [Storage Providers](11_storage_providers.md)
> - [Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)

## Install Templates

DCB ships with .NET templates that scaffold an Aspire+Orleans solution wired for Dynamic Consistency Boundary.

```bash
# Install the Sekiban DCB templates
dotnet new install Sekiban.Dcb.Templates

# Generate a full Orleans host, API, and Blazor front-end
dotnet new sekiban-dcb-orleans -n Contoso.Dcb
```

The template provisions:

- Orleans silo + Aspire AppHost preconfigured for Azure Queue streams
- PostgreSQL container (with migrations project) and Cosmos DB option toggles
- API service exposing command/query endpoints
- Blazor UI that consumes the generated API
- ServiceDefaults project for health checks, logging, and OpenTelemetry

Template reference: `templates/Sekiban.Dcb.Templates/README.md`.

## Solution Layout

```
Contoso.Dcb.Domain/         // Commands, events, tags, projectors, queries
Contoso.Dcb.ApiService/     // Minimal API endpoints, dependency injection
Contoso.Dcb.Web/            // Blazor Server UI
Contoso.Dcb.AppHost/        // Aspire orchestration for silo + dependencies
Contoso.Dcb.ServiceDefaults// Shared hosting extensions
Contoso.Dcb.Tests/          // Unit/integration tests scaffold
```

Compare with the sample domain under `internalUsages/Dcb.Domain` to see a complete configuration.

## Register Domain Types

DCB relies on explicit type registration so the executor can serialize payloads, locate projectors, and materialize
multi-projections. Use `DcbDomainTypes.Simple` to declare your domain:

```csharp
// internalUsages/Dcb.Domain/DomainType.cs
public static DcbDomainTypes GetDomainTypes() =>
    DcbDomainTypes.Simple(types =>
    {
        types.EventTypes.RegisterEventType<StudentCreated>();
        types.TagProjectorTypes.RegisterProjector<StudentProjector>();
        types.TagStatePayloadTypes.RegisterPayloadType<StudentState>();
        types.TagTypes.RegisterTagGroupType<StudentTag>();
        types.MultiProjectorTypes.RegisterProjector<WeatherForecastProjection>();
        types.QueryTypes.RegisterListQuery<GetStudentListQuery>();
    });
```

The template wires this into DI via `builder.Services.AddSingleton(DomainType.GetDomainTypes());`
so Orleans grains and the executor share the same catalog.

## Hook Up the Executor

The API host resolves `ISekibanExecutor` from DI. In Orleans environments use the packaged `OrleansDcbExecutor`
(`src/Sekiban.Dcb.Orleans/OrleansDcbExecutor.cs`):

```csharp
builder.Services.AddSingleton<ISekibanExecutor, OrleansDcbExecutor>();
```

For development without Orleans you can swap in the in-memory executor
(`src/Sekiban.Dcb/InMemory/InMemorySekibanExecutor.cs`) to run commands synchronously in process.

## First Command

Follow the sample `CreateStudent` command in `internalUsages/Dcb.Domain/Student/CreateStudent.cs`:

1. Implement `ICommandWithHandler<T>` and optional data annotations for validation.
2. Query tag state via `ICommandContext` to enforce invariants.
3. Return `EventOrNone.EventWithTags(payload, tags…)` so the executor picks up the business fact and its boundary.

```csharp
public static Task<ResultBox<EventOrNone>> HandleAsync(CreateStudent command, ICommandContext context) =>
    ResultBox.Start
        .Remap(_ => new StudentTag(command.StudentId))
        .Combine(tag => context.TagExistsAsync(tag))
        .Verify((_, exists) => exists ? ExceptionOrNone.FromException(new("Student Already Exists")) : ExceptionOrNone.None)
        .Conveyor((tag, _) => EventOrNone.EventWithTags(
            new StudentCreated(command.StudentId, command.Name, command.MaxClassCount),
            tag));
```

## Run the App

Restore and launch via Aspire:

```bash
dotnet restore
dotnet run --project Contoso.Dcb.AppHost
```

The AppHost spins up the silo, API, Blazor frontend, and backing stores. Navigate to the Aspire dashboard to inspect the
orchestrated services.

## Next Steps

- Flesh out commands/events/tags following the patterns in `internalUsages/Dcb.Domain`
- Implement projections and multi-projections to serve queries
- Expose command/query endpoints (see the API Implementation guide)
