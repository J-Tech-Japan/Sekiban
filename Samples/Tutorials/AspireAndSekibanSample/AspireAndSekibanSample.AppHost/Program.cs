var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("aspirePostgres").AddDatabase("SekibanAspirePostgres");

var apiService = builder.AddProject<Projects.AspireAndSekibanSample_ApiService>("apiservice").WithReference(postgres);

builder.AddProject<Projects.AspireAndSekibanSample_Web>("webfrontend")
    .WithReference(apiService);

builder.Build().Run();
