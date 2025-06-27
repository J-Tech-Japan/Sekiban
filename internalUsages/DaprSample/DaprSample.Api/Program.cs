using DaprSample.Domain;
using DaprSample.Domain.Generated;
using DaprSample.Domain.User.Commands;
using ResultBoxes;
using Sekiban.Pure.Command;
using Sekiban.Pure.Dapr.Extensions;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Executors;
using DaprSample.Api;
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

// Add Sekiban with Dapr
builder.Services.AddSekibanWithDapr(domainTypes, options =>
{
    options.StateStoreName = "sekiban-statestore";
    options.PubSubName = "sekiban-pubsub";
    options.EventTopicName = "domain-events";
    options.ActorIdPrefix = "dapr-sample";
});

// Override the AggregateEventHandlerActor with our simple implementation
builder.Services.AddActors(options =>
{
    // Remove the default and add our simple version
    options.Actors.RegisterActor<SimpleAggregateEventHandlerActor>("AggregateEventHandlerActor");
});

// Add in-memory event storage for testing
builder.Services.AddSingleton<Sekiban.Pure.Events.IEventWriter, DaprSample.Api.InMemoryEventWriter>();
// Use patched event reader to avoid timeout
builder.Services.AddEventHandlerPatch();

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
app.MapActorsHandlers();

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