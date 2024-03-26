var builder = DistributedApplication.CreateBuilder(args);

// var postgres = builder.AddPostgresContainer("aspirePostgres").WithPgAdmin().AddDatabase("SekibanAspirePostgres");

var postgres = builder.AddPostgres(
        "aspirePostgres", 
        password: builder.Configuration["POSTGRES_PASSWORD"])
    .WithVolumeMount("VolumeMount.postgres.data", "/var/lib/postgresql/data")
    .WithPgAdmin()
    .AddDatabase("SekibanAspirePostgres");

var apiServiceWithPostgres = builder.AddProject<Projects.AspireAndSekibanSample_ApiService_Postgres>("apiservicepostgres").WithReference(postgres);

var cosmos = builder.AddConnectionString("SekibanAspireCosmos");
var blob = builder.AddConnectionString("SekibanAspireBlob");

var apiService = builder.AddProject<Projects.AspireAndSekibanSample_ApiService>("apiservice")
    .WithReference(cosmos)
    .WithReference(blob);


builder.AddProject<Projects.AspireAndSekibanSample_Web>("webfrontend")
    .WithReference(apiService)
    .WithReference(apiServiceWithPostgres);

builder.Build().Run();
