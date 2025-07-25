using DaprSekiban.Domain;
using DaprSekiban.Domain.Generated;
using DaprSekiban.Domain.Aggregates.User.Commands;
using DaprSekiban.Domain.Aggregates.User.Queries;
using DaprSekiban.Domain.Aggregates.WeatherForecasts.Commands;
using DaprSekiban.Domain.Aggregates.WeatherForecasts.Queries;
using DaprSekiban.Domain.Projections.Count;
using DaprSekiban.Domain.ValueObjects;
using ResultBoxes;
using Sekiban.Pure.Command;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Dapr.Extensions;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Executors;
using DaprSekiban.ApiService;
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Pure;
using Microsoft.Extensions.Hosting;
using Sekiban.Pure.CosmosDb;
using Sekiban.Pure.Postgres;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.AddConsole();

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
var domainTypes = DaprSekiban.Domain.Generated.DaprSekibanDomainDomainTypes.Generate(DaprSekiban.Domain.DaprSekibanDomainEventsJsonContext.Default.Options);

// Add Sekiban with Dapr - using the original Dapr-based implementation
builder.Services.AddSekibanWithDapr(domainTypes, options =>
{
    options.StateStoreName = "sekiban-eventstore";
    options.PubSubName = "sekiban-pubsub";
    options.EventTopicName = "events.all";  // Changed to match subscription.yaml
});

// Use patched event reader to avoid timeout
builder.Services.AddEventHandlerPatch();

// Register additional custom actors if needed
// Note: AddSekibanWithDapr already registers all core Sekiban actors
builder.Services.Configure<Microsoft.Extensions.Options.IOptions<Dapr.Actors.Runtime.ActorRuntimeOptions>>(options =>
{
    // Any additional actor configuration can go here
});

// Note: In Dapr mode, Sekiban uses Dapr state store for event persistence
// However, database configuration is still needed for registering core services like IEventWriter
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

app.MapActorsHandlers();

// Wait for Dapr sidecar to be ready and actors to be registered
var daprClient = app.Services.GetRequiredService<DaprClient>();
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

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
        break;
    }
    catch (Exception ex)
    {
        await Task.Delay(2000); // Increased retry interval
    }
}

if (!isHealthy)
{
    startupLogger.LogWarning("Dapr health check did not pass within the timeout period.");
}

// Additional wait for actor registration to complete
await Task.Delay(10000); // Give Dapr 10 seconds to register actors

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
app.MapPost("/api/users/create", async ([FromBody]CreateUser command, [FromServices]ISekibanExecutor executor) =>
{
    try
    {
        var result = await executor.CommandAsync(command);
        
        return result.IsSuccess 
            ? Results.Ok(new { success = true, aggregateId = command.UserId, version = result.GetValue().Version })
            : Results.BadRequest(new { success = false, error = result.GetException().Message });
    }
    catch (Exception ex)
    {
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
    var result = await executor.LoadAggregateAsync<DaprSekiban.Domain.Aggregates.User.UserProjector>(
        PartitionKeys.Existing<DaprSekiban.Domain.Aggregates.User.UserProjector>(userId));
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
app.MapGet("/api/weatherforecast", 
        async ([FromQuery] string? waitForSortableUniqueId,[FromQuery] int? pageSize, [FromQuery] int? pageNumber, [FromQuery] string? sortBy, [FromQuery] bool? isAsc, [FromServices] ISekibanExecutor executor) =>
        {
            var query = new WeatherForecastQuery("")
            {
                WaitForSortableUniqueId = waitForSortableUniqueId,
                SortBy = sortBy,
                IsAsc = isAsc ?? false
            };
            var list = await executor.QueryAsync(query).UnwrapBox();
            
            var items = list.Items.AsEnumerable();
            
            // Apply pagination if parameters are provided
            if (pageSize.HasValue && pageNumber.HasValue && pageNumber.Value > 0)
            {
                var skip = (pageNumber.Value - 1) * pageSize.Value;
                items = items.Skip(skip).Take(pageSize.Value);
            }
            
            return items.ToArray();
        })
    .WithOpenApi()
    .WithName("GetWeatherForecast");

app
    .MapPost(
        "/api/inputweatherforecast",
        async (
                [FromBody] InputWeatherForecastCommand command,
                [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.CommandAsync(command);
            return result.ToSimpleCommandResponse().UnwrapBox();
        })
    .WithName("InputWeatherForecast")
    .WithOpenApi();

app
    .MapPost(
        "/api/removeweatherforecast",
        async (
                [FromBody] RemoveWeatherForecastCommand command,
                [FromServices] ISekibanExecutor executor) =>
            await executor.CommandAsync(command).ToSimpleCommandResponse().UnwrapBox())
    .WithName("RemoveWeatherForecast")
    .WithOpenApi();

app
    .MapPost(
        "/api/updateweatherforecastlocation",
        async (
                [FromBody] UpdateWeatherForecastLocationCommand command,
                [FromServices] ISekibanExecutor executor) =>
            await executor.CommandAsync(command).ToSimpleCommandResponse().UnwrapBox())
    .WithName("UpdateWeatherForecastLocation")
    .WithOpenApi();

app.MapGet("/api/weatherCountByLocation/{location}",
        async ([FromRoute] string location, [FromServices] ISekibanExecutor executor) =>
        await executor.QueryAsync(new WeatherCountQuery(location)).UnwrapBox()).WithOpenApi()
    .WithName("GetWeatherCountByLocation")
    .WithDescription("Get the count of weather forecasts by location");


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
            var temperatureC = new TemperatureCelsius(random.Next(-20, 55) + random.NextDouble());
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

// Map default endpoints for Aspire integration
app.MapDefaultEndpoints();

app.Run();

public record UpdateUserNameRequest(string NewName);
public record UpdateLocationRequest(string Location);