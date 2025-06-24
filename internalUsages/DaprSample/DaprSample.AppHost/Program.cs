var builder = DistributedApplication.CreateBuilder(args);

// Add Redis for Dapr state store and pub/sub
var redis = builder.AddRedis("redis");

// Add API project with Dapr sidecar
var api = builder.AddProject<Projects.DaprSample_Api>("api")
    .WithDaprSidecar("sekiban-api")
    .WithReference(redis);

builder.Build().Run();