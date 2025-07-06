using SharedDomain;
using SharedDomain.Generated;
using SharedDomain.Aggregates.User.Commands;
using SharedDomain.Aggregates.User.Queries;
using SharedDomain.Aggregates.WeatherForecasts.Commands;
using SharedDomain.Aggregates.WeatherForecasts.Queries;
using SharedDomain.ValueObjects;
using ResultBoxes;
using Sekiban.Pure.Command;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Dapr.Extensions;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Executors;
using DaprSample.Api;
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Pure;
using Microsoft.Extensions.Hosting;
using Sekiban.Pure.CosmosDb;
using Sekiban.Pure.Postgres;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Add service defaults for Aspire integration
builder.AddServiceDefaults();

// Add services to the container
builder.Services.AddControllers().AddDapr();
builder.Services.AddEndpointsApiExplorer();

// Add OpenAPI services
builder.Services.AddOpenApi();

// Add memory cache for CachedDaprSerializationService - MUST be before AddSekibanWithDapr
builder.Services.AddMemoryCache();

// Generate domain types
var domainTypes = SharedDomainDomainTypes.Generate(SharedDomainEventsJsonContext.Default.Options);

// Add Sekiban with Dapr - using the original Dapr-based implementation
builder.Services.AddSekibanWithDapr(domainTypes, options =>
{
    options.StateStoreName = "sekiban-eventstore";
    options.PubSubName = "sekiban-pubsub";
    options.EventTopicName = "events.all";  // Changed to match subscription.yaml
    options.ActorIdPrefix = "dapr-sample";
});

// Use patched event reader to avoid timeout
builder.Services.AddEventHandlerPatch();

// Register additional custom actors if needed
// Note: AddSekibanWithDapr already registers all core Sekiban actors
builder.Services.Configure<Microsoft.Extensions.Options.IOptions<Dapr.Actors.Runtime.ActorRuntimeOptions>>(options =>
{
    // Any additional actor configuration can go here
});

if (builder.Configuration.GetSection("Sekiban").GetValue<string>("Database")?.ToLower() == "cosmos")
{
    // Cosmos settings
    builder.AddSekibanCosmosDb();
} else
{
    // Postgres settings
    builder.AddSekibanPostgresDb();
}
var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "DaprSample API";
        options.Theme = ScalarTheme.Purple;
        options.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.UseRouting();
app.UseCloudEvents();
app.MapSubscribeHandler();

// Log actor registration before mapping handlers
var logger = app.Services.GetRequiredService<ILogger<Program>>();
try 
{
    var actorOptions = app.Services.GetService<Microsoft.Extensions.Options.IOptions<Dapr.Actors.Runtime.ActorRuntimeOptions>>();
    if (actorOptions?.Value != null && actorOptions.Value.Actors != null)
    {
        logger.LogInformation("=== REGISTERED ACTORS ===");
        var actorCount = 0;
        try 
        {
            // Try to iterate through registered actors
            var actors = actorOptions.Value.Actors;
            logger.LogInformation("Actor registration collection exists: {HasActors}", actors != null);
            actorCount = actors?.Count ?? 0;
            logger.LogInformation("Number of registered actors: {ActorCount}", actorCount);
        }
        catch (Exception innerEx)
        {
            logger.LogError(innerEx, "Error accessing actor collection");
        }
        logger.LogInformation("=== END REGISTERED ACTORS ===");
    }
    else
    {
        logger.LogWarning("ActorRuntimeOptions or Actors is null");
    }
}
catch (Exception ex)
{
    logger.LogError(ex, "Error logging actor registration info");
}

app.MapActorsHandlers();

// Wait for Dapr sidecar to be ready and actors to be registered
var daprClient = app.Services.GetRequiredService<DaprClient>();
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

startupLogger.LogInformation("Waiting for Dapr sidecar to be ready...");

// Log Dapr environment information
startupLogger.LogInformation("Dapr Environment Info:");
startupLogger.LogInformation("  - DAPR_HTTP_PORT: {DaprHttpPort}", Environment.GetEnvironmentVariable("DAPR_HTTP_PORT") ?? "Not Set");
startupLogger.LogInformation("  - DAPR_GRPC_PORT: {DaprGrpcPort}", Environment.GetEnvironmentVariable("DAPR_GRPC_PORT") ?? "Not Set");
startupLogger.LogInformation("  - APP_ID: {AppId}", Environment.GetEnvironmentVariable("APP_ID") ?? "Not Set");
startupLogger.LogInformation("  - Expected Dapr HTTP: http://localhost:3500");
startupLogger.LogInformation("  - App Port: {AppPort}", 5000);

// Wait for basic Dapr health
var maxWaitTime = TimeSpan.FromSeconds(60); // Increased timeout
var waitStartTime = DateTime.UtcNow;
var isHealthy = false;

while (DateTime.UtcNow - waitStartTime < maxWaitTime)
{
    try
    {
        await daprClient.CheckHealthAsync();
        isHealthy = true;
        startupLogger.LogInformation("Dapr health check passed.");
        break;
    }
    catch (Exception ex)
    {
        startupLogger.LogDebug(ex, "Dapr health check failed, retrying...");
        await Task.Delay(2000); // Increased retry interval
    }
}

if (!isHealthy)
{
    startupLogger.LogWarning("Dapr health check did not pass within the timeout period.");
}

// Additional wait for actor registration to complete
startupLogger.LogInformation("Giving Dapr additional time for actor registration...");
await Task.Delay(10000); // Give Dapr 10 seconds to register actors
startupLogger.LogInformation("Actor registration wait complete. Application is ready.");

// Test actor registration by trying to create a proxy
try 
{
    var testActorId = new Dapr.Actors.ActorId("test-actor-id");
    var testProxy = app.Services.GetRequiredService<Dapr.Actors.Client.IActorProxyFactory>()
        .CreateActorProxy<Sekiban.Pure.Dapr.Actors.IAggregateActor>(testActorId, nameof(Sekiban.Pure.Dapr.Actors.AggregateActor));
    startupLogger.LogInformation("Test actor proxy created successfully.");
}
catch (Exception ex)
{
    startupLogger.LogError(ex, "Failed to create test actor proxy: {Error}", ex.Message);
}

// Debug endpoint to check environment variables
app.MapGet("/debug/env", () =>
{
    var envVars = new Dictionary<string, string?>
    {
        ["REDIS_CONNECTION_STRING"] = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING"),
        ["ConnectionStrings__redis"] = builder.Configuration.GetConnectionString("redis"),
        ["APP_ID"] = Environment.GetEnvironmentVariable("APP_ID"),
        ["DAPR_HTTP_PORT"] = Environment.GetEnvironmentVariable("DAPR_HTTP_PORT"),
        ["DAPR_GRPC_PORT"] = Environment.GetEnvironmentVariable("DAPR_GRPC_PORT")
    };
    
    return Results.Ok(envVars);
})
.WithName("GetEnvironmentVariables")
.WithSummary("Get environment variables")
.WithDescription("Debug endpoint to check current environment variables")
.WithTags("Debug");

// Map API endpoints
app.MapPost("/api/users/create", async ([FromBody]CreateUser command, [FromServices]ISekibanExecutor executor, [FromServices]ILogger<Program> logger) =>
{
    logger.LogInformation("=== CreateUser API called with UserId: {UserId}, Name: {Name} ===", command.UserId, command.Name);
    
    try
    {
        logger.LogInformation("About to call executor.CommandAsync...");
        var result = await executor.CommandAsync(command);
        logger.LogInformation("CommandAsync completed. IsSuccess: {IsSuccess}", result.IsSuccess);
        
        return result.IsSuccess 
            ? Results.Ok(new { success = true, aggregateId = command.UserId, version = result.GetValue().Version })
            : Results.BadRequest(new { success = false, error = result.GetException().Message });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Exception in CreateUser API");
        return Results.BadRequest(new { success = false, error = ex.Message });
    }
})
.WithName("CreateUser")
.WithSummary("Create a new user")
.WithDescription("Creates a new user with the specified ID and name")
.WithTags("Users")
.WithOpenApi();

app.MapPost("/api/users/{userId}/update-name", async (Guid userId, UpdateUserNameRequest request, ISekibanExecutor executor) =>
{
    var command = new UpdateUserName(userId, request.NewName);
    var result = await executor.CommandAsync(command);
    return result.IsSuccess 
        ? Results.Ok(new { success = true, aggregateId = userId, version = result.GetValue().Version })
        : Results.BadRequest(new { success = false, error = result.GetException().Message });
})
.WithName("UpdateUserName")
.WithSummary("Update user name")
.WithDescription("Updates the name of an existing user")
.WithTags("Users")
.WithOpenApi();

app.MapGet("/api/users/{userId}", async (Guid userId, ISekibanExecutor executor, [FromServices]SekibanDomainTypes domainTypes) =>
{
    var result = await executor.LoadAggregateAsync<SharedDomain.Aggregates.User.UserProjector>(
        PartitionKeys.Existing<SharedDomain.Aggregates.User.UserProjector>(userId));
    if (!result.IsSuccess)
    {
        return Results.BadRequest(new { success = false, error = result.GetException().Message });
    }
    
    var aggregate = result.GetValue();
    var typed = domainTypes.AggregateTypes.ToTypedPayload(aggregate).UnwrapBox();
    if (aggregate.Version == 0)
    {
        return Results.NotFound(new { success = false, error = "User not found" });
    }
    
    return Results.Ok(new { success = true, data = (dynamic) typed });
})
.WithName("GetUser")
.WithSummary("Get user by ID")
.WithDescription("Retrieves user information by user ID")
.WithTags("Users")
.WithOpenApi();

// Query endpoints
app.MapGet("/api/users/list", async ([FromQuery] string? nameContains, [FromQuery] string? emailContains, [FromQuery] string? waitForSortableUniqueId, [FromServices] ISekibanExecutor executor) =>
{
    var query = new UserListQuery(nameContains ?? "", emailContains ?? "")
    {
        WaitForSortableUniqueId = waitForSortableUniqueId
    };
    var result = await executor.QueryAsync(query);
    
    if (!result.IsSuccess)
    {
        return Results.BadRequest(new { success = false, error = result.GetException().Message });
    }
    
    var list = result.GetValue();
    return Results.Ok(new { success = true, data = list.Items, totalCount = list.Items.Count() });
})
.WithName("GetUserList")
.WithSummary("Get list of users")
.WithDescription("Retrieves a filtered list of users by name and/or email")
.WithTags("Users")
.WithOpenApi();

app.MapGet("/api/users/{userId}/details", async (Guid userId, [FromQuery] string? waitForSortableUniqueId, [FromServices] ISekibanExecutor executor) =>
{
    var query = new UserQuery(userId)
    {
        WaitForSortableUniqueId = waitForSortableUniqueId
    };
    var result = await executor.QueryAsync(query);
    
    return result.IsSuccess 
        ? Results.Ok(new { success = true, data = result.GetValue() })
        : Results.NotFound(new { success = false, error = result.GetException().Message });
})
.WithName("GetUserDetails")
.WithSummary("Get user details by ID")
.WithDescription("Retrieves detailed user information by user ID using query projection")
.WithTags("Users")
.WithOpenApi();

app.MapGet("/api/users/statistics", async ([FromQuery] string? waitForSortableUniqueId, [FromServices] ISekibanExecutor executor) =>
{
    var query = new UserStatisticsQuery()
    {
        WaitForSortableUniqueId = waitForSortableUniqueId
    };
    var result = await executor.QueryAsync(query);
    
    return result.IsSuccess 
        ? Results.Ok(new { success = true, data = result.GetValue() })
        : Results.BadRequest(new { success = false, error = result.GetException().Message });
})
.WithName("GetUserStatistics")
.WithSummary("Get user statistics")
.WithDescription("Retrieves aggregate statistics about all users in the system")
.WithTags("Users")
.WithOpenApi();

// Weather Forecast endpoints
app.MapPost("/api/weatherforecast/input", async ([FromBody] InputWeatherForecastCommand command, [FromServices] ISekibanExecutor executor) =>
{
    var result = await executor.CommandAsync(command).ToSimpleCommandResponse().UnwrapBox();
    return Results.Ok(result);
})
.WithName("InputWeatherForecast")
.WithSummary("Input weather forecast")
.WithDescription("Creates a new weather forecast")
.WithTags("WeatherForecast");

app.MapPost("/api/weatherforecast/{weatherForecastId}/update-location", async (Guid weatherForecastId, [FromBody] UpdateLocationRequest request, [FromServices] ISekibanExecutor executor, [FromServices] ILogger<Program> logger) =>
{
    logger.LogInformation("=== UpdateWeatherForecastLocation API called with WeatherForecastId: {WeatherForecastId}, Location: {Location} ===", weatherForecastId, request.Location);
    var command = new UpdateWeatherForecastLocationCommand(weatherForecastId, request.Location);
    var result = await executor.CommandAsync(command).ToSimpleCommandResponse().UnwrapBox();
    logger.LogInformation("UpdateWeatherForecastLocation result: {Result}", result);
    return Results.Ok(result);
})
.WithName("UpdateWeatherForecastLocation")
.WithSummary("Update weather forecast location")
.WithDescription("Updates the location of an existing weather forecast")
.WithTags("WeatherForecast");

app.MapPost("/api/weatherforecast/{weatherForecastId}/delete", async (Guid weatherForecastId, [FromServices] ISekibanExecutor executor) =>
{
    var command = new DeleteWeatherForecastCommand(weatherForecastId);
    var result = await executor.CommandAsync(command).ToSimpleCommandResponse().UnwrapBox();
    return Results.Ok(result);
})
.WithName("DeleteWeatherForecast")
.WithSummary("Delete weather forecast")
.WithDescription("Marks a weather forecast as deleted")
.WithTags("WeatherForecast");

app.MapPost("/api/weatherforecast/{weatherForecastId}/remove", async (Guid weatherForecastId, [FromServices] ISekibanExecutor executor, [FromServices] ILogger<Program> logger) =>
{
    logger.LogInformation("=== RemoveWeatherForecast API called with WeatherForecastId: {WeatherForecastId} ===", weatherForecastId);
    var command = new RemoveWeatherForecastCommand(weatherForecastId);
    var result = await executor.CommandAsync(command).ToSimpleCommandResponse().UnwrapBox();
    logger.LogInformation("RemoveWeatherForecast result: {Result}", result);
    return Results.Ok(result);
})
.WithName("RemoveWeatherForecast")
.WithSummary("Remove weather forecast")
.WithDescription("Permanently removes a deleted weather forecast")
.WithTags("WeatherForecast");

app.MapGet("/api/weatherforecast", async ([FromQuery] string? waitForSortableUniqueId, [FromServices] ISekibanExecutor executor) =>
{
    var query = new WeatherForecastQuery { WaitForSortableUniqueId = waitForSortableUniqueId };
    var result = await executor.QueryAsync(query);
    return result.UnwrapBox();
})
.WithName("GetWeatherForecasts")
.WithSummary("Get all weather forecasts")
.WithDescription("Retrieves all weather forecasts")
.WithTags("WeatherForecast");

// Helper endpoint to generate weather data
app.MapPost("/api/weatherforecast/generate", async ([FromServices] ISekibanExecutor executor) =>
{
    var summaries = new[] { "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching" };
    var random = new Random();
    var commands = new List<InputWeatherForecastCommand>();

    foreach (var city in new[] { "Seattle", "Tokyo", "Singapore", "Sydney", "London" })
    {
        for (var i = 0; i < 3; i++)
        {
            var date = DateOnly.FromDateTime(DateTime.Now.AddDays(i));
            var temperatureC = new TemperatureCelsius(random.Next(-20, 55));
            var command = new InputWeatherForecastCommand(
                city,
                date,
                temperatureC,
                summaries[random.Next(summaries.Length)]
            );
            commands.Add(command);
        }
    }

    var results = new List<object>();
    foreach (var command in commands)
    {
        var result = await executor.CommandAsync(command).ToSimpleCommandResponse().UnwrapBox();
        results.Add(result);
    }

    return Results.Ok(new { message = "Sample weather data generated", count = results.Count });
})
.WithName("GenerateWeatherData")
.WithSummary("Generate sample weather data")
.WithDescription("Generates sample weather forecast data for testing")
.WithTags("WeatherForecast");

// Health check endpoint for Dapr
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
.WithName("HealthCheck")
.WithSummary("Health check endpoint")
.WithDescription("Returns the health status of the application")
.WithTags("Health");

// Map default endpoints for Aspire integration
app.MapDefaultEndpoints();

app.Run();

public record UpdateUserNameRequest(string NewName);
public record UpdateLocationRequest(string Location);