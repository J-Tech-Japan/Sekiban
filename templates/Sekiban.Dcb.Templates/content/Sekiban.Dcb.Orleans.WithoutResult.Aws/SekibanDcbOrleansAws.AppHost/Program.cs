using Projects;

var builder = DistributedApplication.CreateBuilder(args);

// Add LocalStack for AWS services (DynamoDB + S3)
var localstack = builder
    .AddContainer("localstack", "localstack/localstack", "4.9")
    .WithEndpoint(targetPort: 4566, port: 4566, scheme: "http", name: "edge")
    .WithEnvironment("SERVICES", "dynamodb,s3")
    .WithEnvironment("PERSISTENCE", "1")
    .WithEnvironment("AWS_DEFAULT_REGION", "us-east-1");
// No data volumes in the AWS templates. PERSISTENCE=1 only takes effect on LocalStack editions that
// support persistence (and with a volume mounted at /var/lib/localstack); the community image does not
// restore DynamoDB state, so events are lost on an AppHost restart. Postgres (read models) is kept
// ephemeral as well, so that it never holds rows for events that no longer exist. The snapshot bucket
// is created by the ApiService in Development.

// Materialized view database (kept separate from the DynamoDB event store so the read-side schema
// can evolve independently). Optional — the ApiService only enables the MV runtime when the
// connection string is wired through.
var materializedViewPostgres = builder
    .AddPostgres("dcbOrleansAwsPostgres")
    .AddDatabase("DcbMaterializedViewPostgres");

// Add the API Service with DynamoDB configuration
var apiService = builder
    .AddProject<SekibanDcbOrleansAws_ApiService>("apiservice")
    .WaitFor(localstack)
    .WaitFor(materializedViewPostgres)
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
    .AddProject<SekibanDcbOrleansAws_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService, "apiservice")
    .WaitFor(apiService);

builder.Build().Run();
