var builder = DistributedApplication.CreateBuilder(args);

// var postgres = builder.AddPostgresContainer("aspirePostgres").WithPgAdmin().AddDatabase("SekibanAspirePostgres");

var postgres = builder.AddPostgresContainer(
        "aspirePostgres", 
        password: builder.Configuration["POSTGRES_PASSWORD"])
    .WithVolumeMount("VolumeMount.postgres.data", "/var/lib/postgresql/data", VolumeMountType.Named)
    .WithPgAdmin()
    .AddDatabase("SekibanAspirePostgres");

var apiService = builder.AddProject<Projects.AspireAndSekibanSample_ApiService>("apiservice").WithReference(postgres);

builder.AddProject<Projects.AspireAndSekibanSample_Web>("webfrontend")
    .WithReference(apiService);

builder.Build().Run();
