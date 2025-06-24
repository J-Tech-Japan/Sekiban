var builder = DistributedApplication.CreateBuilder(args);

// Add Redis for Dapr state store and pub/sub
var redis = builder.AddRedis("redis")
    .WithDataVolume();

// Add Dapr with local components path
builder.AddDapr(options =>
{
    options.EnableTelemetry = false;
});

// Add API project with Dapr sidecar
var api = builder.AddProject<Projects.DaprSample_Api>("api")
    .WithExternalHttpEndpoints()
    .WithDaprSidecar("sekiban-api")
    .WithReference(redis);

builder.Build().Run();