
using CommunityToolkit.Aspire.Hosting.Dapr;
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder
    .AddPostgres("daprSekibanPostgres")
    // .WithDataVolume() // Uncomment to use a data volume
    .WithPgAdmin()
    .AddDatabase("SekibanPostgres");

// Add Dapr
builder.AddDapr();

// Add API project with enhanced Dapr configuration + Scheduler support
// Get absolute path to dapr-components directory
var daprComponentsPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "dapr-components"));
var configPath = Path.Combine(daprComponentsPath, "config.yaml");

var api = builder.AddProject<Projects.DaprSekiban_ApiService>("dapr-sekiban-api")
    .WithExternalHttpEndpoints()
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithDaprSidecar(new DaprSidecarOptions
    {
        AppId = "sekiban-api",
        AppPort = 5010,
        DaprHttpPort = 3501,
        DaprGrpcPort = 50002,
        PlacementHostAddress = "localhost:50005",
        SchedulerHostAddress = "localhost:50006", // Enable scheduler for Actor Reminders
        Config = configPath, // Use absolute path to config file
        ResourcesPaths = [daprComponentsPath] // Use absolute path to components directory
    });

// Add Blazor Web project
builder.AddProject<Projects.DaprSekiban_Web>("dapr-sekiban-web")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WaitFor(api);

var app = builder.Build();

app.Run();