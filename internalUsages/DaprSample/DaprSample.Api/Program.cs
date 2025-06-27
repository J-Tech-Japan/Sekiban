using DaprSample.Domain;
using DaprSample.Domain.Generated;
using DaprSample.Domain.User.Commands;
using ResultBoxes;
using Sekiban.Pure.Command;
using Sekiban.Pure.Dapr.Extensions;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Executors;
using DaprSample.Api;
using Dapr.Client;
//using DaprSample.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Add service defaults
//builder.AddServiceDefaults();

// Add services to the container
builder.Services.AddControllers().AddDapr();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add memory cache for CachedDaprSerializationService - MUST be before AddSekibanWithDapr
builder.Services.AddMemoryCache();

// Generate domain types
var domainTypes = DaprSampleDomainDomainTypes.Generate(DaprSampleEventsJsonContext.Default.Options);

// Add Sekiban with Dapr - using the original Dapr-based implementation
builder.Services.AddSekibanWithDapr(domainTypes, options =>
{
    options.StateStoreName = "sekiban-eventstore";
    options.PubSubName = "sekiban-pubsub";
    options.EventTopicName = "domain-events";
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

var app = builder.Build();

// Configure the HTTP request pipeline
//app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
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
});

// Map API endpoints
app.MapPost("/api/users/create", async (CreateUser command, ISekibanExecutor executor, ILogger<Program> logger) =>
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
.WithOpenApi();

app.MapGet("/api/users/{userId}", async (Guid userId, ISekibanExecutor executor) =>
{
    var result = await executor.LoadAggregateAsync<DaprSample.Domain.User.UserProjector>(
        PartitionKeys.Existing<DaprSample.Domain.User.UserProjector>(userId));
    if (!result.IsSuccess)
    {
        return Results.BadRequest(new { success = false, error = result.GetException().Message });
    }
    
    var aggregate = result.GetValue();
    if (aggregate.Version == 0)
    {
        return Results.NotFound(new { success = false, error = "User not found" });
    }
    
    return Results.Ok(new { success = true, data = aggregate.Payload });
})
.WithName("GetUser")
.WithOpenApi();

app.Run();

public record UpdateUserNameRequest(string NewName);