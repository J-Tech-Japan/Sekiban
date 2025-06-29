
using CommunityToolkit.Aspire.Hosting.Dapr;
var builder = DistributedApplication.CreateBuilder(args);

// Add Dapr
builder.AddDapr();

// Add API project with enhanced Dapr configuration
builder.AddProject<Projects.DaprSample_Api>("api")
    .WithExternalHttpEndpoints()
    .WithDaprSidecar(new DaprSidecarOptions
    {
        AppId = "sekiban-api",
        AppPort = 5010,
        DaprHttpPort = 3501,
        DaprGrpcPort = 50002,
        PlacementHostAddress = "localhost:50005",
        SchedulerHostAddress = "",
        ResourcesPaths = [Path.Combine(builder.Environment.ContentRootPath, "..", "dapr-components")]
    });

var app = builder.Build();

app.Run();