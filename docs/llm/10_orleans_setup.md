# Orleans Setup - Sekiban Event Sourcing

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Aggregate, Projector, Command and Events](03_aggregate_command_events.md)
> - [Multiple Aggregate Projector](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Workflow](06_workflow.md)
> - [JSON and Orleans Serialization](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md)
> - [Client API (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md) (You are here)
> - [Unit Testing](11_unit_testing.md)
> - [Common Issues and Solutions](12_common_issues.md)

## Orleans Setup

Microsoft Orleans is a framework that provides a straightforward approach to building distributed high-scale computing applications. Sekiban integrates with Orleans to provide a robust, scalable event sourcing infrastructure.

## Basic Orleans Configuration

### In Program.cs

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Configure Orleans
builder.UseOrleans(siloBuilder =>
{
    // For development, use localhost clustering
    siloBuilder.UseLocalhostClustering();
    
    // Add memory grain storage
    siloBuilder.AddMemoryGrainStorage("PubSubStore");
    
    // Configure serialization
    siloBuilder.Services.AddSingleton<IGrainStorageSerializer, NewtonsoftJsonSekibanOrleansSerializer>();
});

// Register domain types
builder.Services.AddSingleton(
    YourProjectDomainDomainTypes.Generate(
        YourProjectDomainEventsJsonContext.Default.Options));

// Configure database
builder.AddSekibanCosmosDb();  // or AddSekibanPostgresDb();

var app = builder.Build();
// Rest of your app setup
```

## Orleans Clustering Options

### Development: Local Clustering

For development and testing, use localhost clustering:

```csharp
siloBuilder.UseLocalhostClustering();
```

### Production: Azure Table Storage Clustering

For production in Azure environments:

```csharp
siloBuilder.UseAzureStorageClustering(options =>
{
    options.ConfigureTableServiceClient(builder.Configuration["Orleans:StorageConnectionString"]);
});
```

### Production: Kubernetes Clustering

For Kubernetes environments:

```csharp
siloBuilder.UseKubernetesHosting();
```

## Persistent Grain Storage

Orleans needs a storage provider for its grains. Here are several options:

### Memory Storage (Development Only)

```csharp
siloBuilder.AddMemoryGrainStorage("PubSubStore");
```

### Azure Blob Storage

```csharp
siloBuilder.AddAzureBlobGrainStorage(
    "PubSubStore", 
    options => options.ConfigureBlobServiceClient(
        builder.Configuration["Orleans:BlobConnectionString"]));
```

### Custom Storage

```csharp
siloBuilder.AddGrainStorage("PubSubStore", (sp, name) => 
    ActivatorUtilities.CreateInstance<YourCustomStorageProvider>(sp, name, 
        sp.GetRequiredService<IOptions<YourCustomStorageOptions>>()));
```

## Sekiban with Aspire

Sekiban works seamlessly with the .NET Aspire application hosting model:

### AppHost Project Configuration

```csharp
// Program.cs in the AppHost project
var builder = DistributedApplication.CreateBuilder(args);

// Add service defaults from the ServiceDefaults project
var defaultBuilder = builder.AddProject<Projects.ServiceDefaults>("servicedefaults");

// Add the API service
var api = builder.AddProject<Projects.ApiService>("apiservice");

// Add the Web frontend
var web = builder.AddProject<Projects.Web>("web");

// Connect web frontend to the API service
web.WithReference(api);

// Add additional services as needed
var postgres = builder.AddPostgres("postgres");
api.WithReference(postgres);

// Build and run the application
await builder.BuildApplication().RunAsync();
```

### ServiceDefaults Project Configuration

```csharp
// ServiceDefaultsExtensions.cs in the ServiceDefaults project
public static class ServiceDefaultsExtensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        // Add health checks
        builder.AddDefaultHealthChecks();

        // Add service discovery
        builder.Services.AddServiceDiscovery();

        // Add Orleans with distributed configuration
        builder.UseOrleans(siloBuilder =>
        {
            // In development, use localhost clustering
            if (builder.Environment.IsDevelopment())
            {
                siloBuilder.UseLocalhostClustering();
            }
            else
            {
                // In production, use Azure clustering or another production-suitable clustering method
                siloBuilder.UseAzureStorageClustering(options =>
                {
                    options.ConfigureTableServiceClient(
                        builder.Configuration.GetConnectionString("OrleansStorage"));
                });
            }

            // Add a common grain storage provider
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            
            // Configure serialization
            siloBuilder.Services.AddSingleton<IGrainStorageSerializer, NewtonsoftJsonSekibanOrleansSerializer>();
        });

        return builder;
    }
}
```

### API Service Project Configuration

```csharp
// Program.cs in the ApiService project
var builder = WebApplication.CreateBuilder(args);

// Add service defaults from the ServiceDefaults project
builder.AddServiceDefaults();

// Register domain types
builder.Services.AddSingleton(
    YourDomainDomainTypes.Generate(
        YourDomainEventsJsonContext.Default.Options));

// Configure Sekiban database
if (builder.Configuration["Sekiban:Database"] == "Cosmos")
{
    builder.AddSekibanCosmosDb();
}
else
{
    builder.AddSekibanPostgresDb();
}

// Rest of your API service setup...
```

## Orleans Dashboard

For monitoring and administration, you can add the Orleans Dashboard:

```csharp
// Add Orleans Dashboard for monitoring
siloBuilder.UseDashboard(options =>
{
    options.Port = 8081;
    options.HideTrace = true;
    options.CounterUpdateIntervalMs = 10000;
});
```

Then access the dashboard at `http://localhost:8081`.

## Orleans Client Configuration

If you have separate client applications that need to connect to your Orleans cluster:

```csharp
// Configure Orleans Client
builder.UseOrleansClient(clientBuilder =>
{
    // For development, connect to localhost
    if (builder.Environment.IsDevelopment())
    {
        clientBuilder.UseLocalhostClustering();
    }
    else
    {
        // For production, connect to Azure clustering or other production clustering
        clientBuilder.UseAzureStorageClustering(options =>
        {
            options.ConfigureTableServiceClient(
                builder.Configuration.GetConnectionString("OrleansStorage"));
        });
    }
});
```

## Deployment

Sekiban supports deployment to various environments, including local development, staging, and production.

### Local Development

Use the default template configuration with localhost clustering.

### Azure Container Apps

1. Create a container registry and push your images
2. Create an Azure Container App environment
3. Deploy your services as Container Apps
4. Ensure that clustering storage (e.g., Azure Storage Tables) is configured

Example Azure CLI commands:

```bash
# Create resource group
az group create --name your-resource-group --location eastus

# Create Container App environment
az containerapp env create --name your-env --resource-group your-resource-group --location eastus

# Create Azure Storage account for Orleans clustering
az storage account create --name yourstorageaccount --resource-group your-resource-group --location eastus --sku Standard_LRS

# Deploy container app
az containerapp create \
  --name api-service \
  --resource-group your-resource-group \
  --environment your-env \
  --image your-registry.azurecr.io/apiservice:latest \
  --target-port 80 \
  --ingress external \
  --min-replicas 1 \
  --max-replicas 10 \
  --secrets "ORLEANS_STORAGE=storage-connection-string" \
  --env-vars "Orleans__StorageConnectionString=secretref:ORLEANS_STORAGE"
```

### Kubernetes

1. Create Kubernetes cluster
2. Deploy your services using Kubernetes manifests or Helm charts
3. Configure Orleans for Kubernetes clustering

Example YAML file:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: api-service
spec:
  replicas: 3
  selector:
    matchLabels:
      app: api-service
  template:
    metadata:
      labels:
        app: api-service
    spec:
      containers:
      - name: api-service
        image: your-registry.azurecr.io/apiservice:latest
        ports:
        - containerPort: 80
        env:
        - name: ORLEANS__USEKUBERNETESHOSTING
          value: "true"
        - name: SEKIBAN__DATABASE
          value: "Postgres"
        - name: SEKIBAN__POSTGRES__CONNECTIONSTRING
          valueFrom:
            secretKeyRef:
              name: postgres-secrets
              key: connection-string
```

## Scaling Considerations

Orleans is designed for horizontal scaling, but you should consider:

1. **Workload Distribution**: Ensure that your aggregates are properly partitioned
2. **Storage Performance**: Choose appropriate storage providers based on your performance needs
3. **Grain Activation**: Monitor grain activation and deactivation patterns
4. **Resource Allocation**: Allocate appropriate CPU and memory for your Orleans services
