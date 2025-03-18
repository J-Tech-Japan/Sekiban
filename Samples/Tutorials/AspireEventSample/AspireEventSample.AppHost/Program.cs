using Projects;
var builder = DistributedApplication.CreateBuilder(args);

var storage = builder
    .AddAzureStorage("azurestorage")
    // .RunAsEmulator();
    .RunAsEmulator(r => r.WithImage("azure-storage/azurite", "3.34.0"));
var clusteringTable = storage.AddTables("clustering");
var grainStorage = storage.AddBlobs("grain-state");

var postgresPassword = builder.AddParameter("postgres-password", true);
var postgresServer = builder
    .AddPostgres("aspireOrleansPostgres", password: postgresPassword)
    .WithDataVolume("aspireOrleansPostgresData")
    .WithPgAdmin();

// Add databases
var sekibanPostgres = postgresServer.AddDatabase("SekibanPostgres");
var readModelDb = postgresServer.AddDatabase("ReadModel");

var orleans = builder
    .AddOrleans("default")
    .WithClustering(clusteringTable)
    .WithGrainStorage("Default", grainStorage);


var apiService = builder
    .AddProject<AspireEventSample_ApiService>("apiservice")
    .WithEndpoint("https", annotation => annotation.IsProxied = false)
    .WithReference(sekibanPostgres)
    .WithReference(readModelDb)
    .WithReference(orleans);

builder
    .AddProject<AspireEventSample_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
