using Aspire.Hosting.Dapr;

var builder = DistributedApplication.CreateBuilder(args);

// Daprのデフォルトredisを使用せず、独自のRedisを6379ポートで起動
var redis = builder.AddRedis("redis")
    .WithDataVolume();

// Add Dapr
builder.AddDapr();

// Add API project with Dapr sidecar
var api = builder.AddProject<Projects.DaprSample_Api>("api")
    .WithExternalHttpEndpoints()
    .WithReference(redis)
    .WaitFor(redis)
    .WithDaprSidecar();

builder.Build().Run();