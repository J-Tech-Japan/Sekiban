using Projects;
var builder = DistributedApplication.CreateBuilder(args);

// Add Azure Storage emulator for Orleans
var storage = builder
    .AddAzureStorage("azurestorage")
    // .RunAsEmulator(opt => opt.WithDataVolume());
    .RunAsEmulator();
var clusteringTable = storage.AddTables("DcbOrleansClusteringTable");
var grainTable = storage.AddTables("DcbOrleansGrainTable");
var grainStorage = storage.AddBlobs("DcbOrleansGrainState");
var queue = storage.AddQueues("DcbOrleansQueue");

// Add dedicated blob storage for MultiProjection snapshot offloading
var multiProjectionOffload = storage.AddBlobs("MultiProjectionOffload");

// Add PostgreSQL for event storage (optional - can use in-memory for development)
var postgres = builder
    .AddPostgres("dcbOrleansPostgres")
    .WithPgAdmin()
    // .WithDataVolume()
    .AddDatabase("DcbPostgres");

// Configure Orleans
var orleans = builder
    .AddOrleans("default")
    .WithClustering(clusteringTable)
    .WithGrainStorage("Default", grainStorage)
    .WithGrainStorage("OrleansStorage", grainStorage)
    .WithGrainStorage("dcb-orleans-queue", grainStorage)
    .WithGrainStorage("DcbOrleansGrainTable", grainTable)
    .WithStreaming(queue);

// Add the API Service
var apiService = builder
    .AddProject<DcbOrleans_ApiService>("apiservice")
    .WithReference(postgres)
    .WithReference(orleans)
    .WithReference(multiProjectionOffload)
    .WaitFor(postgres);

// Add the Web frontend
builder
    .AddProject<DcbOrleans_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

// Add benchmark project (load generator)
var bench = builder
    .AddProject<DcbOrleans_Benchmark>("bench")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithEnvironment("ApiBaseUrl", apiService.GetEndpoint("http"))
    .WithEnvironment("BENCH_TOTAL", "10000")
    .WithEnvironment("BENCH_CONCURRENCY", "32")
    .WithHttpEndpoint();

builder.Build().Run();
