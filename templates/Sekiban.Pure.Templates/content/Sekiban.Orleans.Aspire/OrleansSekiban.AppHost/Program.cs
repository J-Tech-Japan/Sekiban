using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var storage = builder.AddAzureStorage("azurestorage")
    .RunAsEmulator(r => r.WithImage("azure-storage/azurite", "3.33.0"));
var clusteringTable = storage.AddTables("orleans-sekiban-clustering");
var grainStorage = storage.AddBlobs("orleans-sekiban-grain-state");
var queue = storage.AddQueues("orleans-sekiban-queue");



var postgres = builder
    .AddPostgres("orleansSekibanPostgres")
    .WithDataVolume()
    .WithPgAdmin()
    .AddDatabase("SekibanPostgres");

var orleans = builder.AddOrleans("default")
    .WithClustering(clusteringTable)
    .WithGrainStorage("Default", grainStorage)
    .WithGrainStorage("orleans-sekiban-queue", grainStorage)
    .WithStreaming(queue);

var apiService = builder.AddProject<OrleansSekiban_ApiService>("apiservice")
    // .WithEndpoint("https", annotation => annotation.IsProxied = false)
    .WithReference(postgres)
    .WithReference(orleans)
    .WithReplicas(2);

builder.AddProject<Projects.OrleansSekiban_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
