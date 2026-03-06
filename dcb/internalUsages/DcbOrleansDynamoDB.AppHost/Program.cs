using Projects;

var builder = DistributedApplication.CreateBuilder(args);
const string AzureBlobProvider = "azureblob";
var benchHttpPort = GetEnvInt("BENCH_HTTP_PORT", 5411);

var storage = builder
    .AddAzureStorage("azurestorage")
    .RunAsEmulator();
var clusteringTable = storage.AddTables("DcbOrleansClusteringTable");
var grainTable = storage.AddTables("DcbOrleansGrainTable");
var grainStorage = storage.AddBlobs("DcbOrleansGrainState");
var queue = storage.AddQueues("DcbOrleansQueue");
var multiProjectionOffload = storage.AddBlobs("MultiProjectionOffload");

var postgres = builder
    .AddPostgres("dcbOrleansPostgres")
    .AddDatabase("DcbPostgres");

var orleans = builder
    .AddOrleans("default")
    .WithClustering(clusteringTable)
    .WithGrainStorage("Default", grainStorage)
    .WithGrainStorage("OrleansStorage", grainStorage)
    .WithGrainStorage("dcb-orleans-queue", grainStorage)
    .WithGrainStorage("DcbOrleansGrainTable", grainTable)
    .WithStreaming(queue);

var apiService = builder
    .AddProject<DcbOrleansDynamoDB_WithoutResult_ApiService>("apiservice")
    .WithReference(postgres)
    .WithReference(orleans)
    .WithReference(multiProjectionOffload)
    .WithEnvironment("Sekiban__Database", "postgres")
    .WithEnvironment("Sekiban__ColdEvent__Enabled", "true")
    .WithEnvironment("Sekiban__ColdEvent__Storage__Provider", AzureBlobProvider)
    .WithEnvironment("Sekiban__ColdEvent__Storage__Format", "jsonl")
    .WithEnvironment("Sekiban__ColdEvent__Storage__Type", AzureBlobProvider)
    .WithEnvironment("Sekiban__ColdEvent__Storage__AzureBlobClientName", "MultiProjectionOffload")
    .WithEnvironment("Sekiban__ColdEvent__Storage__AzureContainerName", "multiprojection-cold-events")
    .WaitFor(postgres);

builder
    .AddAzureFunctionsProject<DcbOrleans_Catchup_Functions>("cold-catchup-timer")
    .WithHostStorage(storage)
    .WithReference(postgres)
    .WithReference(multiProjectionOffload)
    .WithEnvironment("Sekiban__Database", "postgres")
    .WithEnvironment("ColdExportTimerSchedule", "0 */10 * * * *")
    .WithEnvironment("ColdExport__Interval", "00:10:00")
    .WithEnvironment("ColdExport__CycleBudget", "00:08:00")
    .WithEnvironment("Sekiban__ColdEvent__Enabled", "true")
    .WithEnvironment("Sekiban__ColdEvent__SegmentMaxEvents", "30000")
    .WithEnvironment("Sekiban__ColdEvent__ExportMaxEventsPerRun", "30000")
    .WithEnvironment("Sekiban__ColdEvent__Storage__Provider", AzureBlobProvider)
    .WithEnvironment("Sekiban__ColdEvent__Storage__Format", "jsonl")
    .WithEnvironment("Sekiban__ColdEvent__Storage__Type", AzureBlobProvider)
    .WithEnvironment("Sekiban__ColdEvent__Storage__AzureBlobClientName", "MultiProjectionOffload")
    .WithEnvironment("Sekiban__ColdEvent__Storage__AzureContainerName", "multiprojection-cold-events")
    .WaitFor(postgres);

builder
    .AddProject<DcbOrleans_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService, "apiservice")
    .WaitFor(apiService);

builder
    .AddProject<DcbOrleans_Benchmark>("bench")
    .WithReference(apiService, "apiservice")
    .WaitFor(apiService)
    .WithEnvironment("ApiBaseUrl", apiService.GetEndpoint("http"))
    .WithEnvironment("BENCH_TOTAL", "10000")
    .WithEnvironment("BENCH_CONCURRENCY", "32")
    .WithHttpEndpoint(port: benchHttpPort);

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
