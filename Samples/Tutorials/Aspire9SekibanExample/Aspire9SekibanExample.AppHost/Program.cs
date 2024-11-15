using Projects;
var builder = DistributedApplication.CreateBuilder(args);



var postgresPassword = builder.AddParameter("postgres-password", true);
var postgres = builder
    .AddPostgres("aspire9Postgres", password: postgresPassword)
    .WithDataVolume("aspire9PostgresData")
    .WithPgAdmin()
    .AddDatabase("SekibanPostgres");



var apiService = builder
    .AddProject<Aspire9SekibanExample_ApiService>("apiservice")
    .WithReference(postgres)
    .WithEndpoint("http", annotation => annotation.IsProxied = false)
    .WithEndpoint("https", annotation => annotation.IsProxied = false);

builder
    .AddProject<Aspire9SekibanExample_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
