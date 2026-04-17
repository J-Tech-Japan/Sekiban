using Projects;

var builder = DistributedApplication.CreateBuilder(args);

// Add LocalStack for AWS services (DynamoDB + S3)
var localstack = builder
    .AddContainer("localstack", "localstack/localstack")
    .WithEndpoint(targetPort: 4566, port: 4566, scheme: "http", name: "edge")
    .WithEnvironment("SERVICES", "dynamodb,s3")
    .WithEnvironment("PERSISTENCE", "1")
    .WithEnvironment("AWS_DEFAULT_REGION", "us-east-1");

// Add PostgreSQL for Identity (authentication) database only
var postgresServer = builder
    .AddPostgres("dcbOrleansPostgres")
    .WithDbGate();

var identityPostgres = postgresServer.AddDatabase("IdentityPostgres");

// Materialized view database (kept separate from the DynamoDB event store so the read-side schema
// can evolve independently). Optional — the ApiService only enables the MV runtime when this
// connection string is wired through.
var materializedViewPostgres = postgresServer.AddDatabase("DcbMaterializedViewPostgres");

// Add the API Service with DynamoDB configuration
var apiService = builder
    .AddProject<SekibanDcbDeciderAws_ApiService>("apiservice")
    .WaitFor(localstack)
    .WaitFor(identityPostgres)
    .WaitFor(materializedViewPostgres)
    .WithReference(identityPostgres)
    .WithReference(materializedViewPostgres)
    .WithEnvironment("Sekiban__Database", "dynamodb")
    .WithEnvironment("Orleans__UseInMemoryStreams", "true")
    .WithEnvironment("AWS__Region", "us-east-1")
    .WithEnvironment("AWS_ACCESS_KEY_ID", "localstack")
    .WithEnvironment("AWS_SECRET_ACCESS_KEY", "localstack")
    .WithEnvironment("DynamoDb__ServiceUrl", localstack.GetEndpoint("edge"))
    .WithEnvironment("S3BlobStorage__ServiceUrl", localstack.GetEndpoint("edge"))
    .WithEnvironment("S3BlobStorage__ForcePathStyle", "true");

// Add the Web frontend
builder
    .AddProject<SekibanDcbDeciderAws_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService, "apiservice")
    .WaitFor(apiService);

// Add the Next.js Web frontend (uses tRPC as BFF within Next.js)
builder
    .AddJavaScriptApp("webnext", "../SekibanDcbDeciderAws.WebNext")
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("API_BASE_URL", apiService.GetEndpoint("http"))
    .WaitFor(apiService);

builder.Build().Run();
