var builder = DistributedApplication.CreateBuilder(args);

// Add Redis for Dapr state store and pub/sub
var redis = builder.AddRedis("redis");

// Add API project with Dapr sidecar
var api = builder.AddProject<Projects.DaprSample_Api>("api")
    .WithDaprSidecar(new DaprSidecarOptions
    {
        AppId = "sekiban-api",
        AppPort = 8080,
        DaprHttpPort = 3500,
        DaprGrpcPort = 50001,
        ComponentsDirectory = "../dapr-components"
    })
    .WithReference(redis);

builder.Build().Run();