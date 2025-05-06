using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var storage = builder.AddAzureStorage("azurestorage")
    .RunAsEmulator();
    // .RunAsEmulator(r => r.WithImage("azure-storage/azurite", "3.33.0")); // no need this line for new template
    var clusteringTable = storage.AddTables("OrleansSekibanClustering");
    var grainTable = storage.AddTables("OrleansSekibanGrainTable");
var grainStorage = storage.AddBlobs("OrleansSekibanGrainState");
var queue = storage.AddQueues("OrleansSekibanQueue");



var postgres = builder
    .AddPostgres("orleansSekibanPostgres")
    // .WithDataVolume() // Uncomment to use a data volume
    .WithPgAdmin()
    .AddDatabase("SekibanPostgres");

var orleans = builder.AddOrleans("default")
    .WithClustering(clusteringTable)
    .WithGrainStorage("Default", grainStorage)
    .WithGrainStorage("OrleansSekibanQueue", grainStorage)
    .WithGrainStorage("OrleansSekibanGrainTable", grainTable)
    .WithStreaming(queue);

var apiService = builder.AddProject<OrleansSekiban_ApiService>("apiservice")
    // .WithEndpoint("https", annotation => annotation.IsProxied = false)
    .WithReference(postgres)
    .WithReference(orleans)
    // .WithReplicas(2); // Uncomment to run with 2 replicas
    ;
builder.AddProject<Projects.OrleansSekiban_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
