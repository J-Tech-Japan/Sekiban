var builder = DistributedApplication.CreateBuilder(args);

// Add Azure Storage emulator for Orleans
var storage = builder.AddAzureStorage("azurestorage")
    .RunAsEmulator();
var clusteringTable = storage.AddTables("DcbOrleansClusteringTable");
var grainTable = storage.AddTables("DcbOrleansGrainTable");
var grainStorage = storage.AddBlobs("DcbOrleansGrainState");
var queue = storage.AddQueues("DcbOrleansQueue");

// Add PostgreSQL for event storage (optional - can use in-memory for development)
var postgres = builder
    .AddPostgres("dcbOrleansPostgres")
    .WithPgAdmin()
    .AddDatabase("DcbPostgres");

// Configure Orleans
var orleans = builder.AddOrleans("default")
    .WithClustering(clusteringTable)
    .WithGrainStorage("Default", grainStorage)
    .WithGrainStorage("dcb-orleans-queue", grainStorage)
    .WithGrainStorage("DcbOrleansGrainTable", grainTable)
    .WithStreaming(queue);

// Add the API Service
var apiService = builder.AddProject<Projects.DcbOrleans_ApiService>("apiservice")
    .WithReference(postgres)
    .WithReference(orleans);

// Add the Web frontend
builder.AddProject<Projects.DcbOrleans_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
