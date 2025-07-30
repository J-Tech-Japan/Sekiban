# Dapr Setup - Sekiban Event Sourcing

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
> - [Orleans Setup](10_orleans_setup.md)
> - [Dapr Setup](11_dapr_setup.md) (You are here)
> - [Unit Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Object](15_value_object.md)
> - [Deployment Guide](16_deployment.md)

## Dapr Setup

Dapr (Distributed Application Runtime) provides a distributed computing approach for Sekiban as an alternative to Orleans. Unlike Orleans, Dapr uses the sidecar pattern to provide distributed capabilities to your applications.

## Basic Dapr Configuration

### In Program.cs

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add service defaults for Aspire integration
builder.AddServiceDefaults();

// Add services to the container
builder.Services.AddControllers().AddDapr();
builder.Services.AddEndpointsApiExplorer();

// Add memory cache for CachedDaprSerializationService - MUST be before AddSekibanWithDapr
builder.Services.AddMemoryCache();

// Generate domain types
var domainTypes = YourProject.Domain.Generated.YourProjectDomainDomainTypes.Generate(
    YourProject.Domain.YourProjectDomainEventsJsonContext.Default.Options);

// Add Sekiban with Dapr
builder.Services.AddSekibanWithDapr(domainTypes, options =>
{
    options.StateStoreName = "sekiban-eventstore";
    options.PubSubName = "sekiban-pubsub";
    options.EventTopicName = "events.all";
});

// Configure database (still needed for registering core services)
if (builder.Configuration.GetSection("Sekiban").GetValue<string>("Database")?.ToLower() == "cosmos")
{
    builder.AddSekibanCosmosDb();
} else
{
    builder.AddSekibanPostgresDb();
}

var app = builder.Build();

// Configure Dapr middleware
app.UseRouting();
app.UseCloudEvents();
app.MapSubscribeHandler();

// Configure Sekiban PubSub Event Relay
var consumerGroup = Environment.GetEnvironmentVariable("SEKIBAN_CONSUMER_GROUP") ?? 
                   (app.Environment.IsDevelopment() ? 
                    "dapr-sample-projectors-dev" : 
                    "dapr-sample-projectors");

app.MapSekibanEventRelay(new SekibanPubSubRelayOptions
{
    PubSubName = "sekiban-pubsub",
    TopicName = "events.all",
    EndpointPath = "/internal/pubsub/events",
    ConsumerGroup = consumerGroup,
    MaxConcurrency = app.Environment.IsDevelopment() ? 3 : 5,
    ContinueOnProjectorFailure = app.Environment.IsDevelopment(),
    EnableDeadLetterQueue = !app.Environment.IsDevelopment(),
    DeadLetterTopic = "events.dead-letter",
    MaxRetryCount = app.Environment.IsDevelopment() ? 1 : 3
});

// Map Actors handlers
app.MapActorsHandlers();

app.Run();
```

## Dapr Components Configuration

Dapr uses component files to configure external services. Create a `dapr-components` directory with the following files:

### State Store Configuration (statestore.yaml)

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: sekiban-eventstore
spec:
  type: state.in-memory  # For development
  version: v1
  metadata:
  - name: actorStateStore
    value: "true"
  - name: actorReminders
    value: "true"
  - name: ttlInSeconds
    value: "0"
scopes:
- sekiban-api
```

### Pub/Sub Configuration (pubsub.yaml)

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: sekiban-pubsub
spec:
  type: pubsub.redis
  version: v1
  metadata:
  - name: redisHost
    value: "localhost:6379"
  - name: redisPassword
    value: ""
```

### Subscription Configuration (subscription.yaml)

```yaml
apiVersion: dapr.io/v2alpha1
kind: Subscription
metadata:
  name: domain-events-subscription
spec:
  topic: events.all
  routes:
    default: /pubsub/events
  pubsubname: sekiban-pubsub
scopes:
- dapr-sample-api
```

### Dapr Configuration (config.yaml)

```yaml
apiVersion: dapr.io/v1alpha1
kind: Configuration
metadata:
  name: daprConfig
spec:
  tracing:
    sampling: "1"
  metric:
    enabled: true
  features:
    - name: actorReentrancy
      enabled: true
    - name: scheduleReminders
      enabled: true
  actors:
    actorIdleTimeout: 1h
    actorScanInterval: 30s
    drainOngoingCallTimeout: 60s
    drainRebalancedActors: true
    reminders:
      storagePartitions: 1
      storageType: "memory"
    reentrancy:
      enabled: true
```

## Aspire Host Configuration for Dapr

When using .NET Aspire with Dapr, configure your AppHost as follows:

```csharp
// Program.cs in AppHost project
using CommunityToolkit.Aspire.Hosting.Dapr;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder
    .AddPostgres("daprSekibanPostgres")
    .WithPgAdmin()
    .AddDatabase("SekibanPostgres");

// Add Dapr
builder.AddDapr();

// Get absolute path to dapr-components directory
var daprComponentsPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "dapr-components"));
var configPath = Path.Combine(daprComponentsPath, "config.yaml");

var api = builder.AddProject<Projects.DaprSekiban_ApiService>("dapr-sekiban-api")
    .WithExternalHttpEndpoints()
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithDaprSidecar(new DaprSidecarOptions
    {
        AppId = "sekiban-api",
        AppPort = 5010,
        DaprHttpPort = 3501,
        DaprGrpcPort = 50002,
        PlacementHostAddress = "localhost:50005",
        SchedulerHostAddress = "localhost:50006",
        Config = configPath,
        ResourcesPaths = [daprComponentsPath]
    });

var app = builder.Build();
app.Run();
```

## Production Considerations for Dapr

### State Store Options

For production, replace the in-memory state store with a persistent option:

**Redis State Store:**
```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: sekiban-eventstore
spec:
  type: state.redis
  version: v1
  metadata:
  - name: redisHost
    value: "your-redis-host:6379"
  - name: redisPassword
    secretKeyRef:
      name: redis-secret
      key: password
  - name: actorStateStore
    value: "true"
```

**PostgreSQL State Store:**
```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: sekiban-eventstore
spec:
  type: state.postgresql
  version: v1
  metadata:
  - name: connectionString
    secretKeyRef:
      name: postgres-secret
      key: connection-string
  - name: actorStateStore
    value: "true"
```

### Scaling Dapr Applications

1. **Horizontal Scaling**: Dapr applications can be scaled horizontally by running multiple instances
2. **Consumer Groups**: Use consumer groups for PubSub to distribute message processing
3. **Actor Placement**: Dapr handles actor placement and load balancing automatically
4. **Resource Management**: Configure appropriate CPU and memory limits for both your app and Dapr sidecar

### Environment Variables for Production

```bash
# Consumer group for scaling
SEKIBAN_CONSUMER_GROUP=production-projectors

# Concurrency control
SEKIBAN_MAX_CONCURRENCY=10

# Error handling
SEKIBAN_STRICT_ERROR_HANDLING=true

# Container Apps specific
CONTAINER_APP_NAME=sekiban-api
CONTAINER_APP_REPLICA_NAME=sekiban-api-replica-1
```

## Deployment Considerations

### Azure Container Apps

When deploying to Azure Container Apps, ensure proper Dapr configuration:

```yaml
# Container App configuration
resources:
  cpu: 1.0
  memory: 2Gi
dapr:
  enabled: true
  appId: "sekiban-api"
  appProtocol: "http"
  appPort: 5010
  enableApiLogging: true
  logLevel: "info"
```

### Kubernetes

For Kubernetes deployment, use Dapr annotations:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: sekiban-api
spec:
  replicas: 3
  selector:
    matchLabels:
      app: sekiban-api
  template:
    metadata:
      labels:
        app: sekiban-api
      annotations:
        dapr.io/enabled: "true"
        dapr.io/app-id: "sekiban-api"
        dapr.io/app-port: "5010"
        dapr.io/config: "daprConfig"
    spec:
      containers:
      - name: sekiban-api
        image: your-registry/sekiban-api:latest
        ports:
        - containerPort: 5010
        env:
        - name: SEKIBAN_CONSUMER_GROUP
          value: "production-projectors"
        - name: SEKIBAN_MAX_CONCURRENCY
          value: "10"
```

## Orleans vs Dapr Comparison

| Feature | Orleans | Dapr |
|---------|---------|------|
| **Architecture** | Integrated framework | Sidecar pattern |
| **Language Support** | Primarily .NET | Language agnostic |
| **State Management** | Built-in grain storage | External state stores |
| **Messaging** | Built-in streaming | External pub/sub |
| **Actor Model** | Virtual actors (grains) | Dapr actors |
| **Deployment** | Single process | App + sidecar |
| **Learning Curve** | Orleans-specific concepts | Dapr + distributed systems |
| **Performance** | Lower latency (direct calls) | Higher latency (HTTP/gRPC) |
| **Ecosystem** | .NET focused | Cloud-native ecosystem |

Choose Orleans for .NET-centric applications requiring maximum performance, or Dapr for multi-language environments and cloud-native deployments.

## Next Steps

- See [Unit Testing](12_unit_testing.md) for testing your Dapr-based Sekiban applications
- Review [Common Issues and Solutions](13_common_issues.md) for troubleshooting
- Learn about [ResultBox](14_result_box.md) for better error handling
