var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.OrleansSekiban_ApiService>("apiservice");

builder.AddProject<Projects.OrleansSekiban_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
