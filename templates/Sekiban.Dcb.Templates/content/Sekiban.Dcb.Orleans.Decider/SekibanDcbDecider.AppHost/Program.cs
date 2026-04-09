using Projects;
var benchmarkProfile = Environment.GetEnvironmentVariable("BENCHMARK_PROFILE");
var isBenchmarkRun = !string.IsNullOrWhiteSpace(benchmarkProfile);
var isStrictBenchmarkProfile = string.Equals(benchmarkProfile, "tagstategrain-memory", StringComparison.OrdinalIgnoreCase);
var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
{
    Args = args,
    DisableDashboard = isStrictBenchmarkProfile,
    EnableResourceLogging = isStrictBenchmarkProfile
});
var apiServicePort = ResolveConfiguredPort(5141, "E2E_API_SERVICE_PORT", "API_SERVICE_PORT");
var webPort = ResolveConfiguredPort(5180, "E2E_WEB_PORT");
var webNextPort = ResolveConfiguredPort(3000, "E2E_WEBNEXT_PORT", "WEBNEXT_PORT");

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
    .AddProject<SekibanDcbDecider_ApiService>("apiservice")
    .WithReference(postgres)
    .WithReference(identityPostgres)
    .WithReference(multiProjectionOffload)
    .WaitFor(postgres)
    .WaitFor(identityPostgres)
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = apiServicePort;
        endpoint.TargetPort = apiServicePort;
        endpoint.UriScheme = "http";
        endpoint.IsProxied = false;
    })
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:" + apiServicePort);

apiService = ApplyTagStateDiagnostics(
    apiService,
    defaultRuntimeLabel: "native");

if (!isStrictBenchmarkProfile)
{
    apiService = apiService.WithReference(orleans);
}

if (isStrictBenchmarkProfile)
{
    apiService = apiService
        .WithEnvironment("Orleans__UseInMemoryStreams", "true")
        .WithEnvironment("Orleans__UseInMemoryGrainStorage", "true")
        .WithEnvironment("SEKIBAN_BENCHMARK_SKIP_USER_RESERVATION_RULES", "true");
}

#if !BENCHMARK_PROFILE_ACTIVE
if (!isBenchmarkRun)
{
    // Add the Web frontend
    builder
        .AddProject<SekibanDcbDecider_Web>("webfrontend")
        .WithExternalHttpEndpoints()
        .WithReference(apiService)
        .WaitFor(apiService)
        .WithEndpoint("http", endpoint =>
        {
            endpoint.Port = webPort;
            endpoint.TargetPort = webPort;
            endpoint.UriScheme = "http";
            endpoint.IsProxied = false;
        })
        .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:" + webPort);

    // Add the Next.js Web frontend (uses tRPC as BFF within Next.js)
    builder
        .AddJavaScriptApp("webnext", "../SekibanDcbDecider.WebNext")
        .WithHttpEndpoint(port: webNextPort, env: "PORT")
        .WithExternalHttpEndpoints()
        .WithEnvironment("API_BASE_URL", apiService.GetEndpoint("http"))
        .WaitFor(apiService);
}
#endif

builder.Build().Run();

static int ResolveConfiguredPort(int defaultPort, params string[] envNames)
{
    foreach (string envName in envNames)
    {
        string? value = Environment.GetEnvironmentVariable(envName);
        if (int.TryParse(value, out int port))
        {
            return port;
        }
    }

    return defaultPort;
}

static IResourceBuilder<ProjectResource> ApplyTagStateDiagnostics(
    IResourceBuilder<ProjectResource> resource,
    string defaultRuntimeLabel)
{
    string? enabled = Environment.GetEnvironmentVariable("SEKIBAN_TAG_STATE_DIAGNOSTICS_ENABLED");
    if (string.IsNullOrWhiteSpace(enabled))
    {
        return resource;
    }

    resource = resource.WithEnvironment("SEKIBAN_TAG_STATE_DIAGNOSTICS_ENABLED", enabled);

    string? slowMs = Environment.GetEnvironmentVariable("SEKIBAN_TAG_STATE_DIAGNOSTICS_SLOW_MS");
    if (!string.IsNullOrWhiteSpace(slowMs))
    {
        resource = resource.WithEnvironment("SEKIBAN_TAG_STATE_DIAGNOSTICS_SLOW_MS", slowMs);
    }

    string? summaryEvery = Environment.GetEnvironmentVariable("SEKIBAN_TAG_STATE_DIAGNOSTICS_SUMMARY_EVERY");
    if (!string.IsNullOrWhiteSpace(summaryEvery))
    {
        resource = resource.WithEnvironment("SEKIBAN_TAG_STATE_DIAGNOSTICS_SUMMARY_EVERY", summaryEvery);
    }

    string? projectors = Environment.GetEnvironmentVariable("SEKIBAN_TAG_STATE_DIAGNOSTICS_PROJECTORS");
    if (!string.IsNullOrWhiteSpace(projectors))
    {
        resource = resource.WithEnvironment("SEKIBAN_TAG_STATE_DIAGNOSTICS_PROJECTORS", projectors);
    }

    string? outputPath = Environment.GetEnvironmentVariable("SEKIBAN_TAG_STATE_DIAGNOSTICS_FILE");
    if (!string.IsNullOrWhiteSpace(outputPath))
    {
        resource = resource.WithEnvironment("SEKIBAN_TAG_STATE_DIAGNOSTICS_FILE", outputPath);
    }

    string runtimeLabel = Environment.GetEnvironmentVariable("SEKIBAN_TAG_STATE_DIAGNOSTICS_RUNTIME_LABEL")
        ?? defaultRuntimeLabel;
    return resource.WithEnvironment("SEKIBAN_TAG_STATE_DIAGNOSTICS_RUNTIME_LABEL", runtimeLabel);
}
