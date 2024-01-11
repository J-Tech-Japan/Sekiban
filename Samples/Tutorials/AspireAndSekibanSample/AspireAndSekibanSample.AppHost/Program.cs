var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.AspireAndSekibanSample_ApiService>("apiservice");

builder.AddProject<Projects.AspireAndSekibanSample_Web>("webfrontend")
    .WithReference(apiService);

builder.Build().Run();
