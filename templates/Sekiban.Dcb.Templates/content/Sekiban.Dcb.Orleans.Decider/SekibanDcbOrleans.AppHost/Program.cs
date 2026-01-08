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
var postgresServer = builder
    .AddPostgres("dcbOrleansPostgres")
    // .WithPgAdmin()
    // .WithDataVolume()
    .WithDbGate();

// Sekiban event store database
var postgres = postgresServer.AddDatabase("DcbPostgres");

// Identity database (separate from Sekiban to avoid EnsureCreated conflicts)
var identityPostgres = postgresServer.AddDatabase("IdentityPostgres");

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
    .WithReference(identityPostgres)
    .WithReference(orleans)
    .WithReference(multiProjectionOffload)
    .WaitFor(postgres)
    .WaitFor(identityPostgres);

// Add the Web frontend
builder
    .AddProject<SekibanDcbOrleans_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

// Add the Next.js Web frontend (uses tRPC as BFF within Next.js)
builder
    .AddJavaScriptApp("webnext", "../SekibanDcbOrleans.WebNext")
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("API_BASE_URL", apiService.GetEndpoint("http"))
    .WaitFor(apiService);

builder.Build().Run();
