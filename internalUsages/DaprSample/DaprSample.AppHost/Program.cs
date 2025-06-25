var builder = DistributedApplication.CreateBuilder(args);

// Add Redis for Dapr state store and pub/sub
var redis = builder.AddRedis("redis")
    .WithDataVolume();

// Add Dapr
builder.AddDapr(options =>
{
    options.EnableTelemetry = false;
});

// Create Dapr component definitions
var stateStore = builder.AddDaprComponent(
    "sekiban-statestore",
    "state.redis",
    new()
    {
        ["redisHost"] = redis.GetConnectionString(),
        ["redisPassword"] = "",
        ["actorStateStore"] = "true"
    });

var pubSub = builder.AddDaprComponent(
    "sekiban-pubsub",
    "pubsub.redis", 
    new()
    {
        ["redisHost"] = redis.GetConnectionString(),
        ["redisPassword"] = ""
    });

// Add API project with Dapr sidecar
var api = builder.AddProject<Projects.DaprSample_Api>("api")
    .WithExternalHttpEndpoints()
    .WithDaprSidecar("sekiban-api")
    .WithReference(redis)
    .WithReference(stateStore)
    .WithReference(pubSub);

builder.Build().Run();