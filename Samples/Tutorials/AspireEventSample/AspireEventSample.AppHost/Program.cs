using Projects;
var builder = DistributedApplication.CreateBuilder(args);

var storage = builder
    .AddAzureStorage("azurestorage")
    .RunAsEmulator();
// .RunAsEmulator(r => r.WithImage("azure-storage/azurite", "3.34.0"));
var clusteringTable = storage.AddTables("OrleansSekibanClustering");
var grainTable = storage.AddTables("OrleansSekibanGrainTable");
var grainStorage = storage.AddBlobs("OrleansSekibanGrainState");
var queue = storage.AddQueues("OrleansSekibanQueue");

var postgresPassword = builder.AddParameter("postgres-password", true);
var postgresServer = builder
    .AddPostgres("aspireOrleansPostgres", password: postgresPassword)
    // .WithDataVolume("aspireOrleansPostgresData")
    .WithPgAdmin();

// Add databases
var sekibanPostgres = postgresServer.AddDatabase("SekibanPostgres");
var readModelDb = postgresServer.AddDatabase("ReadModel");

var orleans = builder
    .AddOrleans("default")
    .WithClustering(clusteringTable)
    .WithGrainStorage("Default", grainStorage)
    .WithGrainStorage("orleans-sekiban-queue", grainStorage)
    .WithGrainStorage("OrleansSekibanGrainTable", grainTable)
    .WithStreaming(queue);

var apiService = builder
        .AddProject<AspireEventSample_ApiService>("apiservice")
        .WithEndpoint("https", annotation => annotation.IsProxied = false)
        .WithReference(sekibanPostgres)
        .WithReference(readModelDb)
        .WithReference(orleans)
        .WaitFor(sekibanPostgres)
        .WaitFor(readModelDb)
    ;

builder
    .AddProject<AspireEventSample_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();