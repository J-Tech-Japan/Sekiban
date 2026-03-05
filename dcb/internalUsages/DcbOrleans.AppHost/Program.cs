using Projects;

var builder = DistributedApplication.CreateBuilder(args);
var benchHttpPort = GetEnvInt("BENCH_HTTP_PORT", 5411);

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
    .WithDbGate()
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

#if false

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
    .WithReference(apiService, "apiservice")
    .WaitFor(apiService);

// Add benchmark project (load generator)
var bench = builder
    .AddProject<DcbOrleans_Benchmark>("bench")
    .WithReference(apiService, "apiservice")
    .WaitFor(apiService)
    .WithEnvironment("ApiBaseUrl", apiService.GetEndpoint("http"))
    .WithEnvironment("BENCH_TOTAL", "10000")
    .WithEnvironment("BENCH_CONCURRENCY", "32")
    .WithHttpEndpoint(port: benchHttpPort);

#else

// Add the WithoutResult API Service
var withoutResultApiService = builder
    .AddProject<DcbOrleans_WithoutResult_ApiService>("withoutresultapiservice")
    .WithReference(postgres)
    .WithReference(orleans)
    .WithReference(multiProjectionOffload)
    .WaitFor(postgres);

builder
    .AddProject<DcbOrleans_Catchup_Functions>("cold-catchup-timer")
    .WithReference(withoutResultApiService, "apiservice")
    .WithEnvironment("ApiBaseUrl", withoutResultApiService.GetEndpoint("http"))
    .WithEnvironment("ColdExport:Interval", "00:03:00")
    .WithEnvironment("ColdExport:RequestTimeout", "00:05:00")
    .WaitFor(withoutResultApiService);

// Add the Web frontend
builder
    .AddProject<DcbOrleans_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(withoutResultApiService, "apiservice")
    .WaitFor(withoutResultApiService);

// Add benchmark project (load generator)
var bench = builder
    .AddProject<DcbOrleans_Benchmark>("bench")
    .WithReference(withoutResultApiService, "apiservice")
    .WaitFor(withoutResultApiService)
    .WithEnvironment("ApiBaseUrl", withoutResultApiService.GetEndpoint("http"))
    .WithEnvironment("BENCH_TOTAL", "10000")
    .WithEnvironment("BENCH_CONCURRENCY", "32")
    .WithHttpEndpoint(port: benchHttpPort);

#endif

// Sekiban DCB CLI Tools (manual execution only)
// Use Aspire dashboard "Start" button to run these tools
builder
    .AddProject<DcbOrleans_Cli>("projection-status")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithArgs("status")
    .WithEnvironment("VERBOSE", "true")
    .WithExplicitStart();

builder
    .AddProject<DcbOrleans_Cli>("projection-list")
    .WithArgs("list")
    .WithExplicitStart();

builder
    .AddProject<DcbOrleans_Cli>("projection-build")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithArgs("build", "--verbose")
    .WithEnvironment("MIN_EVENTS", "1000")
    .WithExplicitStart();

builder
    .AddProject<DcbOrleans_Cli>("projection-build-force")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithArgs("build", "--force", "--verbose")
    .WithExplicitStart();

builder.Build().Run();

static int GetEnvInt(string name, int defaultValue)
    => int.TryParse(Environment.GetEnvironmentVariable(name), out var parsed) ? parsed : defaultValue;
