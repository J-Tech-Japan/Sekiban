using SharedDomain;
using SharedDomain.Generated;
using SharedDomain.Aggregates.User.Commands;
using SharedDomain.Aggregates.User.Queries;
using SharedDomain.Aggregates.WeatherForecasts;
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
using Microsoft.AspNetCore.Routing;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Add service defaults for Aspire integration
builder.AddServiceDefaults();

// Add services to the container
builder.Services.AddControllers()
    .AddDapr();
builder.Services.AddEndpointsApiExplorer();

// Add OpenAPI services
builder.Services.AddOpenApi();

// Add memory cache for CachedDaprSerializationService - MUST be before AddSekibanWithDapr
builder.Services.AddMemoryCache();

// Generate domain types
var domainTypes = SharedDomainDomainTypes.Generate(SharedDomainEventsJsonContext.Default.Options);

// Add Sekiban with Dapr - using the original Dapr-based implementation
var actorIdPrefix = Environment.GetEnvironmentVariable("SEKIBAN_ACTOR_PREFIX") ?? 
                    Environment.GetEnvironmentVariable("CONTAINER_APP_NAME") ?? 
                    (builder.Environment.IsDevelopment() ? 
                     $"local-dev-{Environment.MachineName}" : 
                     "dapr-sample");

builder.Services.AddSekibanWithDapr(domainTypes, options =>
{
    options.StateStoreName = "sekiban-eventstore";
    options.PubSubName = "sekiban-pubsub";
    options.EventTopicName = "events.all";  // Changed to match subscription.yaml
    options.ActorIdPrefix = actorIdPrefix;
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

// === Sekiban PubSub Event Relay (MinimalAPI) ===
var instanceId = Environment.GetEnvironmentVariable("CONTAINER_APP_REPLICA_NAME") ?? 
                Environment.GetEnvironmentVariable("HOSTNAME") ?? 
                Environment.MachineName ?? 
                Guid.NewGuid().ToString("N")[..8];

var consumerGroup = Environment.GetEnvironmentVariable("SEKIBAN_CONSUMER_GROUP") ?? 
                   (app.Environment.IsDevelopment() ? 
                    "dapr-sample-projectors-dev" : 
                    "dapr-sample-projectors");

var continueOnFailure = app.Environment.IsDevelopment() || 
                       !bool.TryParse(Environment.GetEnvironmentVariable("SEKIBAN_STRICT_ERROR_HANDLING"), out var strictMode) || 
                       !strictMode;

var maxConcurrency = int.TryParse(Environment.GetEnvironmentVariable("SEKIBAN_MAX_CONCURRENCY"), out var concurrency) ? 
                    concurrency : 
                    (app.Environment.IsDevelopment() ? 3 : 5);

app.MapSekibanEventRelay(new SekibanPubSubRelayOptions
{
    PubSubName = "sekiban-pubsub",
    TopicName = "events.all",
    EndpointPath = "/internal/pubsub/events",
    ConsumerGroup = consumerGroup,
    MaxConcurrency = maxConcurrency,
    ContinueOnProjectorFailure = continueOnFailure,
    EnableDeadLetterQueue = !app.Environment.IsDevelopment(),
    DeadLetterTopic = "events.dead-letter",
    MaxRetryCount = app.Environment.IsDevelopment() ? 1 : 3
});

// Log actor registration before mapping handlers
var logger = app.Services.GetRequiredService<ILogger<Program>>();

// Log the configured PubSub relay information
logger.LogInformation("=== SEKIBAN PUBSUB RELAY CONFIGURED ({Environment} ENVIRONMENT) ===", app.Environment.EnvironmentName);
logger.LogInformation("Instance ID: {InstanceId}", instanceId);
logger.LogInformation("Actor ID Prefix: {ActorIdPrefix}", actorIdPrefix);
logger.LogInformation("PubSub Component: sekiban-pubsub");
logger.LogInformation("Topic: events.all");
logger.LogInformation("Endpoint: /internal/pubsub/events");
logger.LogInformation("Consumer Group: {ConsumerGroup}", consumerGroup);
logger.LogInformation("Max Concurrency: {MaxConcurrency}", maxConcurrency);
logger.LogInformation("Continue On Failure: {ContinueOnFailure}", continueOnFailure);
logger.LogInformation("Dead Letter Queue: {DeadLetterEnabled}", !app.Environment.IsDevelopment());
if (app.Environment.IsDevelopment())
{
    logger.LogInformation("🔧 LOCAL DEVELOPMENT MODE: Relaxed settings for easier debugging");
}
else
{
    logger.LogInformation("🚀 PRODUCTION MODE: Strict settings for reliability");
}
logger.LogInformation("=== END PUBSUB RELAY CONFIG ===");
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
startupLogger.LogInformation("=== DAPR & ENVIRONMENT INFO ({Environment}) ===", app.Environment.EnvironmentName);
startupLogger.LogInformation("  - DAPR_HTTP_PORT: {DaprHttpPort}", Environment.GetEnvironmentVariable("DAPR_HTTP_PORT") ?? "Not Set (Default: 3500)");
startupLogger.LogInformation("  - DAPR_GRPC_PORT: {DaprGrpcPort}", Environment.GetEnvironmentVariable("DAPR_GRPC_PORT") ?? "Not Set (Default: 50001)");
startupLogger.LogInformation("  - APP_ID: {AppId}", Environment.GetEnvironmentVariable("APP_ID") ?? "Not Set");
startupLogger.LogInformation("  - Expected Dapr HTTP: http://localhost:3500");
startupLogger.LogInformation("  - App Port: {AppPort}", 5000);

if (app.Environment.IsDevelopment())
{
    startupLogger.LogInformation("=== LOCAL DEVELOPMENT ENVIRONMENT INFO ===");
    startupLogger.LogInformation("  - Machine Name: {MachineName}", Environment.MachineName);
    startupLogger.LogInformation("  - User Name: {UserName}", Environment.UserName ?? "Not Set");
    startupLogger.LogInformation("  - OS Version: {OSVersion}", Environment.OSVersion);
    startupLogger.LogInformation("  - Process ID: {ProcessId}", Environment.ProcessId);
}
else
{
    // ACA specific environment variables
    startupLogger.LogInformation("=== ACA ENVIRONMENT INFO ===");
    startupLogger.LogInformation("  - CONTAINER_APP_NAME: {ContainerAppName}", Environment.GetEnvironmentVariable("CONTAINER_APP_NAME") ?? "Not Set");
    startupLogger.LogInformation("  - CONTAINER_APP_REPLICA_NAME: {ReplicaName}", Environment.GetEnvironmentVariable("CONTAINER_APP_REPLICA_NAME") ?? "Not Set");
    startupLogger.LogInformation("  - CONTAINER_APP_REVISION: {Revision}", Environment.GetEnvironmentVariable("CONTAINER_APP_REVISION") ?? "Not Set");
}

startupLogger.LogInformation("=== SEKIBAN CONFIGURATION ===");
startupLogger.LogInformation("  - HOSTNAME: {Hostname}", Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName);
startupLogger.LogInformation("  - Instance ID: {InstanceId}", instanceId);
startupLogger.LogInformation("  - Actor ID Prefix: {ActorIdPrefix}", actorIdPrefix);
startupLogger.LogInformation("  - Consumer Group: {ConsumerGroup}", consumerGroup);
startupLogger.LogInformation("  - Max Concurrency: {MaxConcurrency}", maxConcurrency);
startupLogger.LogInformation("  - Continue On Failure: {ContinueOnFailure}", continueOnFailure);
startupLogger.LogInformation("=== END ENVIRONMENT INFO ===");

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
        // Basic Environment
        ["Environment"] = app.Environment.EnvironmentName,
        ["MachineName"] = Environment.MachineName,
        ["UserName"] = Environment.UserName,
        ["ProcessId"] = Environment.ProcessId.ToString(),
        
        // Dapr Environment
        ["REDIS_CONNECTION_STRING"] = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING"),
        ["ConnectionStrings__redis"] = builder.Configuration.GetConnectionString("redis"),
        ["APP_ID"] = Environment.GetEnvironmentVariable("APP_ID"),
        ["DAPR_HTTP_PORT"] = Environment.GetEnvironmentVariable("DAPR_HTTP_PORT"),
        ["DAPR_GRPC_PORT"] = Environment.GetEnvironmentVariable("DAPR_GRPC_PORT"),
        
        // Sekiban Configuration
        ["SEKIBAN_CONSUMER_GROUP"] = Environment.GetEnvironmentVariable("SEKIBAN_CONSUMER_GROUP"),
        ["SEKIBAN_ACTOR_PREFIX"] = Environment.GetEnvironmentVariable("SEKIBAN_ACTOR_PREFIX"),
        ["SEKIBAN_MAX_CONCURRENCY"] = Environment.GetEnvironmentVariable("SEKIBAN_MAX_CONCURRENCY"),
        ["SEKIBAN_STRICT_ERROR_HANDLING"] = Environment.GetEnvironmentVariable("SEKIBAN_STRICT_ERROR_HANDLING")
    };
    
    if (!app.Environment.IsDevelopment())
    {
        envVars.Add("CONTAINER_APP_NAME", Environment.GetEnvironmentVariable("CONTAINER_APP_NAME"));
        envVars.Add("CONTAINER_APP_REPLICA_NAME", Environment.GetEnvironmentVariable("CONTAINER_APP_REPLICA_NAME"));
        envVars.Add("CONTAINER_APP_REVISION", Environment.GetEnvironmentVariable("CONTAINER_APP_REVISION"));
    }
    
    envVars.Add("HOSTNAME", Environment.GetEnvironmentVariable("HOSTNAME"));
    
    return Results.Ok(envVars);
})
.WithName("GetEnvironmentVariables")
.WithSummary("Get environment variables")
.WithDescription("Debug endpoint to check current environment variables (Local Dev & ACA compatible)")
.WithTags("Debug");

// Debug endpoint to check PubSub relay configuration
app.MapGet("/debug/pubsub-config", () =>
{
    var config = new
    {
        // Basic Configuration
        Environment = app.Environment.EnvironmentName,
        PubSubComponent = "sekiban-pubsub",
        Topic = "events.all", 
        Endpoint = "/internal/pubsub/events",
        ConsumerGroup = consumerGroup,
        MaxConcurrency = maxConcurrency,
        ContinueOnFailure = continueOnFailure,
        RelayMethod = "MinimalAPI (opt-in)",
        ConfiguredAt = DateTime.UtcNow,
        
        // Environment-specific Configuration
        InstanceId = instanceId,
        ActorIdPrefix = actorIdPrefix,
        ScaleOutReady = true,
        DeadLetterQueue = !app.Environment.IsDevelopment(),
        DeadLetterTopic = "events.dead-letter",
        MaxRetryCount = app.Environment.IsDevelopment() ? 1 : 3,
        
        // Runtime Environment
        MachineName = Environment.MachineName,
        Hostname = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName,
        
        Note = app.Environment.IsDevelopment() ? 
               "🔧 Local Development: Relaxed settings for easier debugging" :
               "🚀 Production: Configured for ACA scale-out with Consumer Group to prevent duplicate processing"
    };
    
    if (!app.Environment.IsDevelopment())
    {
        var acaInfo = new
        {
            ContainerAppName = Environment.GetEnvironmentVariable("CONTAINER_APP_NAME"),
            ReplicaName = Environment.GetEnvironmentVariable("CONTAINER_APP_REPLICA_NAME"),
            Revision = Environment.GetEnvironmentVariable("CONTAINER_APP_REVISION")
        };
        
        return Results.Ok(new { config, aca = acaInfo });
    }
    
    return Results.Ok(config);
})
.WithName("GetPubSubConfiguration")
.WithSummary("Get PubSub relay configuration")
.WithDescription("Debug endpoint to check current PubSub relay configuration (Local Dev & ACA compatible)")
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

// Test endpoint to get individual aggregate state directly
app.MapGet("/api/weatherforecast/{weatherForecastId}/aggregate-state", async (Guid weatherForecastId, [FromServices] ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("=== GetAggregateState API called with WeatherForecastId: {WeatherForecastId} ===", weatherForecastId);
        
        // Use HttpClient to call Dapr actor directly
        using var httpClient = new HttpClient();
        httpClient.BaseAddress = new Uri($"http://localhost:{Environment.GetEnvironmentVariable("DAPR_HTTP_PORT") ?? "3500"}");
        
        // Try different actor ID formats
        var actorIdFormats = new[]
        {
            $"local-dev-Mac-WeatherForecastProjector-{weatherForecastId}-default",
            $"WeatherForecastProjector-{weatherForecastId}-default",
            $"{weatherForecastId}"
        };
        
        foreach (var actorIdFormat in actorIdFormats)
        {
            try
            {
                logger.LogInformation("Trying actor ID format: {ActorId}", actorIdFormat);
                
                // Call the actor method directly via HTTP
                var response = await httpClient.PutAsync(
                    $"/v1.0/actors/AggregateActor/{actorIdFormat}/method/GetAggregateStateAsync",
                    new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    logger.LogInformation("Success with actor ID: {ActorId}, Response: {Response}", actorIdFormat, content);
                    
                    return Results.Ok(new 
                    { 
                        success = true,
                        aggregateId = weatherForecastId,
                        actorId = actorIdFormat,
                        rawResponse = content
                    });
                }
                else
                {
                    logger.LogWarning("Failed with actor ID: {ActorId}, Status: {Status}", actorIdFormat, response.StatusCode);
                }
            }
            catch (Exception innerEx)
            {
                logger.LogWarning(innerEx, "Error with actor ID format: {ActorId}", actorIdFormat);
            }
        }
        
        return Results.NotFound(new { success = false, error = "Actor not found with any ID format" });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error getting aggregate state for WeatherForecastId: {WeatherForecastId}", weatherForecastId);
        return Results.BadRequest(new { success = false, error = ex.Message });
    }
})
.WithName("GetWeatherForecastAggregateState")
.WithSummary("Get weather forecast aggregate state directly from actor")
.WithDescription("Test endpoint to verify individual aggregate actors are working")
.WithTags("WeatherForecast");

// Simple test endpoint to check aggregate version
app.MapGet("/api/weatherforecast/{weatherForecastId}/version", async (Guid weatherForecastId, [FromServices] ISekibanExecutor executor, [FromServices] ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("=== CheckAggregateVersion API called with WeatherForecastId: {WeatherForecastId} ===", weatherForecastId);
        
        // Try to execute a simple query command to check if aggregate exists
        var command = new UpdateWeatherForecastLocationCommand(weatherForecastId, "test");
        var result = await executor.CommandAsync(command).Conveyor(r => ResultBox.FromValue(r));
        
        if (result.IsSuccess)
        {
            var response = result.GetValue();
            return Results.Ok(new 
            { 
                success = true,
                aggregateId = weatherForecastId,
                version = response.Version,
                exists = true
            });
        }
        else
        {
            return Results.Ok(new 
            { 
                success = true,
                aggregateId = weatherForecastId,
                version = 0,
                exists = false,
                error = result.GetException().Message
            });
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error checking aggregate version for WeatherForecastId: {WeatherForecastId}", weatherForecastId);
        return Results.BadRequest(new { success = false, error = ex.Message });
    }
})
.WithName("GetWeatherForecastVersion")
.WithSummary("Get weather forecast aggregate version")
.WithDescription("Simple endpoint to check if aggregate exists by version")
.WithTags("WeatherForecast");

// Health check endpoint for Dapr
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
.WithName("HealthCheck")
.WithSummary("Health check endpoint")
.WithDescription("Returns the health status of the application")
.WithTags("Health");

// Enhanced health check for ACA scaling
app.MapGet("/health/detailed", () =>
{
    var health = new
    {
        status = "healthy",
        timestamp = DateTime.UtcNow,
        instance = new
        {
            id = instanceId,
            hostname = Environment.GetEnvironmentVariable("HOSTNAME"),
            replicaName = Environment.GetEnvironmentVariable("CONTAINER_APP_REPLICA_NAME"),
            revision = Environment.GetEnvironmentVariable("CONTAINER_APP_REVISION")
        },
        sekiban = new
        {
            actorIdPrefix = actorIdPrefix,
            consumerGroup = consumerGroup,
            pubsubEndpoint = "/internal/pubsub/events"
        },
        dapr = new
        {
            httpPort = Environment.GetEnvironmentVariable("DAPR_HTTP_PORT"),
            grpcPort = Environment.GetEnvironmentVariable("DAPR_GRPC_PORT"),
            appId = Environment.GetEnvironmentVariable("APP_ID")
        }
    };
    
    return Results.Ok(health);
})
.WithName("DetailedHealthCheck")
.WithSummary("Detailed health check for ACA scaling")
.WithDescription("Returns detailed health status including instance and configuration information")
.WithTags("Health");

// Event monitoring endpoint - tracks PubSub events
var eventCounter = 0;
var lastEventTime = DateTime.MinValue;
var eventLog = new Queue<string>(100); // Keep last 100 events

app.MapPost("/internal/pubsub/events/monitor", async (HttpContext context, [FromServices] ILogger<Program> logger) =>
{
    var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
    eventCounter++;
    lastEventTime = DateTime.UtcNow;
    
    var logEntry = $"[{lastEventTime:yyyy-MM-dd HH:mm:ss.fff}] Event #{eventCounter}: {body.Substring(0, Math.Min(200, body.Length))}...";
    eventLog.Enqueue(logEntry);
    if (eventLog.Count > 100) eventLog.Dequeue();
    
    logger.LogInformation("=== PUBSUB EVENT RECEIVED (Monitor) ===");
    logger.LogInformation("Event Count: {EventCount}", eventCounter);
    logger.LogInformation("Body Preview: {Body}", body.Substring(0, Math.Min(500, body.Length)));
    
    return Results.Ok();
})
.WithName("EventMonitor")
.WithSummary("Monitor PubSub events")
.WithDescription("Endpoint to monitor incoming PubSub events for debugging");

app.MapGet("/api/debug/event-stats", () =>
{
    return Results.Ok(new
    {
        totalEvents = eventCounter,
        lastEventTime = lastEventTime == DateTime.MinValue ? "No events received" : lastEventTime.ToString("yyyy-MM-dd HH:mm:ss.fff"),
        timeSinceLastEvent = lastEventTime == DateTime.MinValue ? "N/A" : $"{(DateTime.UtcNow - lastEventTime).TotalSeconds:F1} seconds ago",
        recentEvents = eventLog.Take(10).ToArray()
    });
})
.WithName("EventStats")
.WithSummary("Get event statistics")
.WithDescription("Returns statistics about received PubSub events")
.WithTags("Debug");

// Test endpoint to verify multi-projector pub/sub flow
app.MapPost("/api/test/pubsub-flow", async ([FromServices] ISekibanExecutor executor, [FromServices] ILogger<Program> testLogger) =>
{
    testLogger.LogInformation("=== Testing Pub/Sub Flow ===");
    
    // Create a test user - this should trigger pub/sub
    var userId = Guid.NewGuid();
    var createCommand = new CreateUser(userId, "PubSub Test User", $"pubsub-{userId}@test.com");
    
    testLogger.LogInformation("Creating user with ID: {UserId}", userId);
    var result = await executor.CommandAsync(createCommand);
    
    if (!result.IsSuccess)
    {
        return Results.BadRequest(new { error = result.GetException().Message });
    }
    
    testLogger.LogInformation("User created successfully. Version: {Version}", result.GetValue().Version);
    
    // Wait a bit for pub/sub to propagate
    await Task.Delay(1000);
    
    // Try to query using the multi-projector
    testLogger.LogInformation("Querying user list to verify multi-projection...");
    var query = new UserListQuery("", "");
    var queryResult = await executor.QueryAsync(query);
    
    if (!queryResult.IsSuccess)
    {
        return Results.Ok(new 
        { 
            userCreated = true,
            version = result.GetValue().Version,
            projectionAvailable = false,
            error = queryResult.GetException().Message
        });
    }
    
    var users = queryResult.GetValue().Items.ToList();
    var foundUser = users.FirstOrDefault(u => u.UserId == userId);
    
    return Results.Ok(new 
    { 
        userCreated = true,
        version = result.GetValue().Version,
        projectionAvailable = foundUser != null,
        totalUsers = users.Count,
        testUserId = userId,
        foundInProjection = foundUser != null ? new { foundUser.UserId, foundUser.Name, foundUser.Email } : null,
        message = foundUser != null 
            ? "✅ Pub/Sub working! User found in multi-projection." 
            : "⚠️ User created but not yet in multi-projection. Pub/Sub might not be working."
    });
})
.WithName("TestPubSubFlow")
.WithSummary("Test pub/sub event flow")
.WithDescription("Creates a user and verifies it appears in multi-projections via pub/sub")
.WithTags("Debug");

// Map default endpoints for Aspire integration
app.MapDefaultEndpoints();

// Note: app.MapSubscribeHandler() already maps the /dapr/subscribe endpoint
// The EventPubSubController from Sekiban.Pure.Dapr should provide the subscriptions

// Log all registered endpoints
var endpointDataSource = app.Services.GetRequiredService<EndpointDataSource>();
logger.LogInformation("=== Registered Endpoints ===");
foreach (var endpoint in endpointDataSource.Endpoints)
{
    if (endpoint is RouteEndpoint routeEndpoint)
    {
        logger.LogInformation("Route: {Pattern}, HTTP: {Methods}", 
            routeEndpoint.RoutePattern.RawText,
            routeEndpoint.Metadata.OfType<HttpMethodMetadata>().FirstOrDefault()?.HttpMethods ?? new[] { "ANY" });
    }
}

app.Run();

public record UpdateUserNameRequest(string NewName);
public record UpdateLocationRequest(string Location);
