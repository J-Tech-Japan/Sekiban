using Projects;
var builder = DistributedApplication.CreateBuilder(args);

// Add Azure Storage emulator for Orleans
var storage = builder
    .AddAzureStorage("azurestorage")
    // Named data volume: tables, blobs and queues survive an AppHost restart.
    .RunAsEmulator(opt => opt.WithDataVolume());
var clusteringTable = storage.AddTables("DcbOrleansClusteringTable");
var grainTable = storage.AddTables("DcbOrleansGrainTable");
var grainStorage = storage.AddBlobs("DcbOrleansGrainState");
var queue = storage.AddQueues("DcbOrleansQueue");

// Add dedicated blob storage for MultiProjection snapshot offloading
var multiProjectionOffload = storage.AddBlobs("MultiProjectionOffload");

// Add PostgreSQL for event storage (optional - can use in-memory for development)
var postgresServer = builder
    .AddPostgres("dcbOrleansPostgres")
    // Named data volume: events survive an AppHost restart. The generated password is kept in the
    // AppHost user secrets, so it stays the same across restarts and matches the volume.
    .WithDataVolume()
    .WithPgAdmin()
    .WithDbGate();
var postgres = postgresServer.AddDatabase("DcbPostgres");

// Materialized view database (kept separate from the event store so that the read-side schema can
// evolve without touching the source-of-truth event log).
var materializedViewPostgres = postgresServer.AddDatabase("DcbMaterializedViewPostgres");

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
    .AddProject<SekibanDcbOrleans_ApiService>("apiservice")
    .WithReference(postgres)
    .WithReference(materializedViewPostgres)
    .WithReference(orleans)
    .WithReference(multiProjectionOffload)
    .WaitFor(postgres)
    .WaitFor(materializedViewPostgres);

// Add the Web frontend
builder
    .AddProject<SekibanDcbOrleans_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
