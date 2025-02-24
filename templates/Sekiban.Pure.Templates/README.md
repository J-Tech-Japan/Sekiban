# Orleans Sekiban Event Sourcing Sample Project.

## how to install from nuget.
dotnet new install Sekiban.Pure.Template

## how to make project

dotnet new sekiban-orleans-aspire -n YourProjectName

## 1. App Host need to add app setting (secrets.json) postgres password.

```json
secrets.json
{
  "Parameters:postgres-password": "your_strong_password"
}
```

## 2. Optional : cluster setting etc needs to change by project

AppHost.Program.cs

```cs
var storage = builder.AddAzureStorage("azurestorage")
    .RunAsEmulator(r => r.WithImage("azure-storage/azurite", "3.33.0"));
var clusteringTable = storage.AddTables("orleans-sekiban-clustering");
var grainStorage = storage.AddBlobs("orleans-sekiban-grain-state");

var postgresPassword = builder.AddParameter("postgres-password", true);
var postgres = builder
    .AddPostgres("orleansSekibanPostgres", password: postgresPassword)
    .WithDataVolume("orleansSekibanPostgresData")
    .WithPgAdmin()
    .AddDatabase("SekibanPostgres");

var orleans = builder.AddOrleans("default")
    .WithClustering(clusteringTable)
    .WithGrainStorage("Default", grainStorage);


var apiService = builder.AddProject<OrleansSekiban_ApiService>("apiservice")
    .WithEndpoint("https", annotation => annotation.IsProxied = false)
    .WithReference(postgres)
    .WithReference(orleans);

```

AppService.Program.cs as well
```cs
builder.AddKeyedAzureTableClient("orleans-sekiban-clustering");
builder.AddKeyedAzureBlobClient("orleans-sekiban-grain-state");
builder.UseOrleans(
    config =>
    {
        config.UseDashboard(options => { });
        config.AddMemoryStreams("EventStreamProvider").AddMemoryGrainStorage("EventStreamProvider");
    });

```