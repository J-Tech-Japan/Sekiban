using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var localstack = builder
    .AddContainer("localstack", "localstack/localstack")
    .WithEndpoint(targetPort: 4566, port: 4566, scheme: "http", name: "edge")
    .WithEnvironment("SERVICES", "dynamodb,s3")
    .WithEnvironment("PERSISTENCE", "1")
    .WithEnvironment("AWS_DEFAULT_REGION", "us-east-1");

var apiService = builder
    .AddProject<DcbOrleansDynamoDB_WithoutResult_ApiService>("dynamodb-apiservice")
    .WaitFor(localstack)
    .WithEnvironment("Sekiban__Database", "dynamodb")
    .WithEnvironment("Orleans__UseInMemoryStreams", "true")
    .WithEnvironment("AWS__Region", "us-east-1")
    .WithEnvironment("AWS_ACCESS_KEY_ID", "localstack")
    .WithEnvironment("AWS_SECRET_ACCESS_KEY", "localstack")
    .WithEnvironment("DynamoDb__ServiceUrl", localstack.GetEndpoint("edge"))
    .WithEnvironment("S3BlobStorage__ServiceUrl", localstack.GetEndpoint("edge"))
    .WithEnvironment("S3BlobStorage__ForcePathStyle", "true");

builder
    .AddProject<DcbOrleans_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService, "apiservice")
    .WaitFor(apiService);

builder
    .AddProject<DcbOrleans_Benchmark>("bench")
    .WithReference(apiService, "apiservice")
    .WaitFor(apiService)
    .WithEnvironment("ApiBaseUrl", apiService.GetEndpoint("http"))
    .WithEnvironment("BENCH_TOTAL", "10000")
    .WithEnvironment("BENCH_CONCURRENCY", "32")
    .WithHttpEndpoint();

builder.Build().Run();
