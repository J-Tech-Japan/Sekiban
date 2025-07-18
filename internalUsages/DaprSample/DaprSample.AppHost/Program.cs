
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

// Log the paths for debugging
Console.WriteLine($"=== DAPR COMPONENTS CONFIGURATION ===");
Console.WriteLine($"ContentRootPath: {builder.Environment.ContentRootPath}");
Console.WriteLine($"DaprComponentsPath: {daprComponentsPath}");
Console.WriteLine($"ConfigPath: {configPath}");
Console.WriteLine($"DaprComponentsPath Exists: {Directory.Exists(daprComponentsPath)}");
Console.WriteLine($"ConfigPath Exists: {File.Exists(configPath)}");
if (Directory.Exists(daprComponentsPath))
{
    Console.WriteLine($"Files in DaprComponentsPath:");
    foreach (var file in Directory.GetFiles(daprComponentsPath))
    {
        Console.WriteLine($"  - {Path.GetFileName(file)}");
    }
}
Console.WriteLine($"=====================================");

var api = builder.AddProject<Projects.DaprSample_Api>("dapr-sample-api")
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

// Add EventRelay project with Dapr sidecar
var eventRelay = builder.AddProject<Projects.DaprSample_EventRelay>("dapr-sample-event-relay")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithDaprSidecar(new DaprSidecarOptions
    {
        AppId = "sekiban-event-relay",
        AppPort = 5020,
        DaprHttpPort = 3502,
        DaprGrpcPort = 50003,
        PlacementHostAddress = "localhost:50005",
        SchedulerHostAddress = "localhost:50006", // Enable scheduler for Actor Reminders
        Config = configPath, // Use absolute path to config file
        ResourcesPaths = [daprComponentsPath] // Use absolute path to components directory
    });

// Add Blazor Web project
builder.AddProject<Projects.DaprSample_Web>("dapr-sample-web")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WaitFor(api);

var app = builder.Build();

app.Run();