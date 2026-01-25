using Projects;

var builder = DistributedApplication.CreateBuilder(args);

// Add LocalStack for AWS services (DynamoDB + S3)
var localstack = builder
    .AddContainer("localstack", "localstack/localstack")
    .WithEndpoint(targetPort: 4566, port: 4566, scheme: "http", name: "edge")
    .WithEnvironment("SERVICES", "dynamodb,s3")
    .WithEnvironment("PERSISTENCE", "1")
    .WithEnvironment("AWS_DEFAULT_REGION", "us-east-1");

// Add the API Service with DynamoDB configuration
var apiService = builder
    .AddProject<SekibanDcbOrleansAws_ApiService>("apiservice")
    .WaitFor(localstack)
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
