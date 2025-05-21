# API Implementation - Sekiban Event Sourcing

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Aggregate, Projector, Command and Events](03_aggregate_command_events.md)
> - [Multiple Aggregate Projector](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Workflow](06_workflow.md)
> - [JSON and Orleans Serialization](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md) (You are here)
> - [Client API (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md)
> - [Unit Testing](11_unit_testing.md)
> - [Common Issues and Solutions](12_common_issues.md)

## API Implementation

### Basic Setup Pattern

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// 1. Configure Orleans
builder.UseOrleans(config =>
{
    // set your own orleans settings
});

// 2. Register Domain
builder.Services.AddSingleton(
    YourProjectDomainDomainTypes.Generate(
        YourProjectDomainEventsJsonContext.Default.Options));

// 3. Configure Database
builder.AddSekibanCosmosDb();  // or AddSekibanPostgresDb();

// 4. Map Endpoints
var app = builder.Build();
var apiRoute = app.MapGroup("/api");

// Command endpoint pattern
apiRoute.MapPost("/command",
    async ([FromBody] YourCommand command, 
           [FromServices] SekibanOrleansExecutor executor) => 
        await executor.CommandAsync(command).ToSimpleCommandResponse().UnwrapBox());

// Query endpoint pattern
apiRoute.MapGet("/query",
    async ([FromServices] SekibanOrleansExecutor executor) =>
    {
        var result = await executor.QueryAsync(new YourQuery())
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

This pattern ensures your UI always reflects the most recent state changes, providing a more consistent user experience.

### Organizing API Endpoints

For larger applications, you may want to organize your API endpoints into separate files:

```csharp
// UserEndpoints.cs
namespace YourProject.Api.Endpoints;

public static class UserEndpoints
{
    public static WebApplication MapUserEndpoints(this WebApplication app)
    {
        var apiGroup = app.MapGroup("/api/users").WithTags("Users");
        
        // Register user
        apiGroup.MapPost("/register",
            async ([FromBody] RegisterUserCommand command, [FromServices] SekibanOrleansExecutor executor) =>
                await executor.CommandAsync(command).ToSimpleCommandResponse().UnwrapBox())
            .WithName("RegisterUser")
            .WithOpenApi();
            
        // Get user details
        apiGroup.MapGet("/{userId}",
            async (Guid userId, [FromServices] SekibanOrleansExecutor executor) =>
            {
                var result = await executor.QueryAsync(new GetUserDetailsQuery(userId)).UnwrapBox();
                return result is null ? Results.NotFound() : Results.Ok(result);
            })
            .WithName("GetUserDetails")
            .WithOpenApi();
            
        return app;
    }
}
```

### Configuration

```json
{
  "Sekiban": {
    "Database": "Cosmos"  // or "Postgres"
  }
}
```

For more specific database configuration:

```json
{
  "Sekiban": {
    "Database": "Cosmos",
    "Cosmos": {
      "ConnectionString": "your-connection-string",
      "DatabaseName": "your-database-name"
    }
  }
}
```
