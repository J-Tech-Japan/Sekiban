using System.Net;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Dcb.EventSource;
using Orleans.Configuration;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Runtime.Hosting;
using Orleans.Storage;
using Npgsql;
using Scalar.AspNetCore;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.BlobStorage.AzureStorage;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.ServiceId;
using SekibanDcbDecider.ApiService.Endpoints;
using SekibanDcbDecider.ApiService.Auth;
using SekibanDcbDecider.ApiService;
using SekibanDcbDecider.ApiService.Exceptions;
using SekibanDcbDecider.ApiService.Health;
using SekibanDcbDecider.ApiService.Realtime;

const string InMemoryStreamsConfigKey = "Orleans:UseInMemoryStreams";
const string InMemoryGrainStorageConfigKey = "Orleans:UseInMemoryGrainStorage";
const string EnsurePostgresDatabaseExistsConfigKey = "Postgres:EnsureDatabaseExists";
const string PubSubStoreName = "PubSubStore";
const string PostgresAdminDatabaseName = "postgres";
const string DuplicateDatabaseSqlState = "42P04";
const string InsufficientPrivilegeSqlState = "42501";
const int MaxEnsureDatabaseAttempts = 20;

var builder = WebApplication.CreateBuilder(args);

// Configure logging to suppress Azure Storage warnings in development
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddFilter("Azure.Core", LogLevel.Error);
    builder.Logging.AddFilter("Azure.Storage", LogLevel.Error);
    builder.Logging.AddFilter("Orleans.AzureUtils", LogLevel.Error);
}

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Add global exception handler
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Add Authentication & Identity
var authConnectionString = builder.Configuration.GetConnectionString("IdentityPostgres")
    ?? throw new InvalidOperationException("PostgreSQL connection string 'IdentityPostgres' not found");
if (builder.Configuration.GetValue<bool>(EnsurePostgresDatabaseExistsConfigKey))
{
    await EnsurePostgresDatabaseExistsAsync(builder.Configuration.GetConnectionString("DcbPostgres"));
    await EnsurePostgresDatabaseExistsAsync(authConnectionString);
}
builder.Services.AddAuthServices(builder.Configuration, authConnectionString);
builder.Services.AddHostedService<AuthDbInitializer>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Determine clustering type from configuration
var orleansClusterType = builder.Configuration["ORLEANS_CLUSTERING_TYPE"]?.ToLower() ?? "azurestorage";
var orleansGrainType = builder.Configuration["ORLEANS_GRAIN_DEFAULT_TYPE"]?.ToLower() ?? "blob";
var orleansQueueType = builder.Configuration["ORLEANS_QUEUE_TYPE"]?.ToLower() ?? "azurestorage";
var useInMemoryStreams = builder.Configuration.GetValue<bool>(InMemoryStreamsConfigKey);
var useInMemoryGrainStorage = builder.Configuration.GetValue<bool>(InMemoryGrainStorageConfigKey);

// Add Azure Storage clients for Orleans only when NOT using Cosmos for clustering
// (Aspire clients add health checks that require connection strings)
if (!builder.Environment.IsDevelopment() && orleansClusterType != "cosmos")
{
    builder.AddKeyedAzureTableServiceClient("DcbOrleansClusteringTable");
}

// Table client for grain storage (used for checkpointer, PubSub, etc.)
if (!useInMemoryGrainStorage && orleansGrainType != "cosmos")
{
    builder.AddKeyedAzureTableServiceClient("DcbOrleansGrainTable");
    builder.AddKeyedAzureBlobServiceClient("DcbOrleansGrainState");
}

// Blob storage for projection offload (always needed)
builder.AddKeyedAzureBlobServiceClient("MultiProjectionOffload");

// Queue client only when using Azure Storage queues
if (!useInMemoryStreams && orleansQueueType != "eventhub")
{
    builder.AddKeyedAzureQueueServiceClient("DcbOrleansQueue");
}
// Note: Orleans handles EventHub connection directly via AddEventHubStreams
// No need to register Aspire EventHub clients (they add health checks that require extra config)

var databaseType = builder.Configuration.GetSection("Sekiban").GetValue<string>("Database")?.ToLower();

Console.WriteLine($"[Orleans Config] ClusterType: {orleansClusterType}, GrainType: {orleansGrainType}, QueueType: {orleansQueueType}, InMemoryStreams: {useInMemoryStreams}, InMemoryGrainStorage: {useInMemoryGrainStorage}");

if (useInMemoryGrainStorage)
{
    builder.UseOrleans(siloBuilder =>
    {
        ConfigureClusterOptions(siloBuilder, builder.Configuration);
        ConfigureEndpoints(siloBuilder, builder.Environment);
        ConfigureClusteringAndStorage(siloBuilder, builder.Configuration, builder.Environment);
        ConfigureStreaming(siloBuilder, builder.Configuration, orleansQueueType);
        ConfigureGrainStorage(siloBuilder, builder.Configuration);
        ConfigureOrleansServices(siloBuilder, builder.Environment, databaseType, builder.Configuration);
    });
}
else
{
    // Configure Orleans
    builder.Host.UseOrleans((context, siloBuilder) =>
    {
        ConfigureClusterOptions(siloBuilder, context.Configuration);

        // Configure endpoints for Azure Container Apps (non-development environments)
        ConfigureEndpoints(siloBuilder, context.HostingEnvironment);

        ConfigureClusteringAndStorage(siloBuilder, context.Configuration, context.HostingEnvironment);
        ConfigureStreaming(siloBuilder, context.Configuration, orleansQueueType);
        ConfigureGrainStorage(siloBuilder, context.Configuration);
        ConfigureOrleansServices(siloBuilder, context.HostingEnvironment, databaseType, context.Configuration);
    });
}

// Orleans health check registration (Readiness: Silo has joined the cluster)
builder.Services.AddHealthChecks()
    .AddCheck<OrleansHealthCheck>("orleans", tags: ["ready"]);

var domainTypes = DomainType.GetDomainTypes();
builder.Services.AddSingleton(domainTypes);

// Register native runtime abstraction interfaces
builder.Services.AddSekibanDcbNativeRuntime();
builder.Services.AddSekibanDcbColdEventDefaults();

builder.Services.AddSingleton<SseTopicHub>();
builder.Services.AddHostedService<OrleansStreamEventRouter>();

// Configure database storage based on configuration
string? configuredDatabasePath = null;
if (databaseType == "cosmos")
{
    builder.Services.AddSekibanDcbCosmosDbWithAspire();
    builder.Services.AddSingleton<IMultiProjectionStateStore, Sekiban.Dcb.CosmosDb.CosmosMultiProjectionStateStore>();
}
else if (databaseType == "sqlite")
{
    configuredDatabasePath = Path.Combine(Directory.GetCurrentDirectory(), "events.db");
    builder.Services.AddSekibanDcbSqlite(configuredDatabasePath);
}
else
{
    builder.Services.AddSingleton<IEventStore, PostgresEventStore>();
    builder.Services.AddSekibanDcbPostgresWithAspire();
    builder.Services.AddSingleton<IMultiProjectionStateStore, Sekiban.Dcb.Postgres.PostgresMultiProjectionStateStore>();
}

builder.Services.AddTransient<IGrainStorageSerializer, NewtonsoftJsonDcbOrleansSerializer>();
builder.Services.AddTransient<NewtonsoftJsonDcbOrleansSerializer>();
builder.Services.AddSingleton<IStreamDestinationResolver>(sp =>
    new DefaultOrleansStreamDestinationResolver("EventStreamProvider", "AllEvents", Guid.Empty));
builder.Services.AddSingleton<IEventSubscriptionResolver>(sp =>
    new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
builder.Services.AddSingleton<IEventPublisher, OrleansEventPublisher>();
builder.Services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
{
    var blobServiceClient = sp.GetRequiredKeyedService<BlobServiceClient>("MultiProjectionOffload");
    return new AzureBlobStorageSnapshotAccessor(blobServiceClient, "multiprojection-snapshot-offload");
});
builder.Services.AddTransient<ISekibanExecutor, OrleansDcbExecutor>();
builder.Services.AddScoped<IActorObjectAccessor, OrleansActorObjectAccessor>();

// Add CORS services
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

// Log startup configuration
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
startupLogger.LogInformation("Database type: {DatabaseType}", databaseType ?? "postgres");
if (configuredDatabasePath is not null)
    startupLogger.LogInformation("SQLite database path: {DatabasePath}", configuredDatabasePath);

var apiRoute = app.MapGroup("/api");

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Map endpoints
app.MapAuthEndpoints();
apiRoute.MapStudentEndpoints();
apiRoute.MapClassRoomEndpoints();
apiRoute.MapEnrollmentEndpoints();
apiRoute.MapWeatherEndpoints();
apiRoute.MapProjectionEndpoints();
apiRoute.MapDebugEndpoints();
apiRoute.MapRoomEndpoints();
apiRoute.MapReservationEndpoints();
apiRoute.MapApprovalEndpoints();
apiRoute.MapUserDirectoryEndpoints();
apiRoute.MapStreamEndpoints();
apiRoute.MapTestDataEndpoints();

app.MapDefaultEndpoints();

app.Run();

// ============================================================================
// Orleans Configuration Functions
// ============================================================================

void ConfigureClusteringAndStorage(ISiloBuilder siloBuilder, IConfiguration configuration, IHostEnvironment environment)
{
    var cosmosConnection = configuration.GetConnectionString("OrleansCosmos");

    if (!string.IsNullOrWhiteSpace(cosmosConnection))
    {
        ConfigureCosmosOrleans(siloBuilder, cosmosConnection, configuration);
        return;
    }

    // Development or Azure Storage mode
    ConfigureLocalOrleans(siloBuilder, environment, configuration);
}

void ConfigureClusterOptions(ISiloBuilder siloBuilder, IConfiguration configuration)
{
    var orleansSection = configuration.GetSection("Orleans");
    var clusterId = orleansSection["ClusterId"];
    var serviceId = orleansSection["ServiceId"];

    if (string.IsNullOrEmpty(clusterId) && string.IsNullOrEmpty(serviceId))
    {
        return;
    }

    siloBuilder.Configure<ClusterOptions>(options =>
    {
        if (!string.IsNullOrEmpty(clusterId))
        {
            options.ClusterId = clusterId;
            Console.WriteLine($"[Orleans] ClusterId: {clusterId}");
        }
        if (!string.IsNullOrEmpty(serviceId))
        {
            options.ServiceId = serviceId;
            Console.WriteLine($"[Orleans] ServiceId: {serviceId}");
        }
    });
}

void ConfigureCosmosOrleans(ISiloBuilder siloBuilder, string cosmosConnection, IConfiguration configuration)
{
    Console.WriteLine("[Orleans] Using Cosmos clustering");
    var endpointPart = cosmosConnection.Split(';').FirstOrDefault(p => p.StartsWith("AccountEndpoint"));
    Console.WriteLine($"[Orleans] Cosmos endpoint: {endpointPart}");

    siloBuilder.UseCosmosClustering(options =>
    {
        options.ConfigureCosmosClient(cosmosConnection);
        options.IsResourceCreationEnabled = true;
    });

    // Azure Container Apps creates new containers on each deploy,
    // old silo entries remain in Cosmos DB membership table causing restart loops
    siloBuilder.Configure<ClusterMembershipOptions>(options =>
    {
        options.DefunctSiloExpiration = TimeSpan.FromMinutes(5);
        options.DefunctSiloCleanupPeriod = TimeSpan.FromMinutes(1);
        options.NumMissedTableIAmAliveLimit = 2;
    });

    siloBuilder.UseCosmosReminderService(options =>
    {
        options.ConfigureCosmosClient(cosmosConnection);
        options.IsResourceCreationEnabled = true;
    });

    // Configure Cosmos grain storage
    void AddCosmosStore(string name)
    {
        siloBuilder.AddCosmosGrainStorage(name, options =>
        {
            options.ConfigureCosmosClient(cosmosConnection);
            options.IsResourceCreationEnabled = true;
        });
    }

    siloBuilder.AddCosmosGrainStorageAsDefault(options =>
    {
        options.ConfigureCosmosClient(cosmosConnection);
        options.IsResourceCreationEnabled = true;
    });

    foreach (var store in new[] { "OrleansStorage", "dcb-orleans-queue", "DcbOrleansGrainTable", "EventStreamProvider" })
    {
        AddCosmosStore(store);
    }

    // PubSubStore for Cosmos is registered conditionally based on stream type
    // (in-memory streams use MemoryGrainStorage, others use Cosmos)
    var useInMemoryStreams = configuration.GetValue<bool>(InMemoryStreamsConfigKey);
    if (!useInMemoryStreams)
    {
        AddCosmosStore(PubSubStoreName);
    }
}

void ConfigureLocalOrleans(ISiloBuilder siloBuilder, IHostEnvironment environment, IConfiguration configuration)
{
    var useInMemoryGrainStorage = configuration.GetValue<bool>(InMemoryGrainStorageConfigKey);

    if (environment.IsDevelopment())
    {
        Console.WriteLine("[Orleans] Using LocalhostClustering (Development mode)");
        siloBuilder.UseLocalhostClustering();
    }
    else
    {
        Console.WriteLine("[Orleans] Using Azure Table Storage clustering");
        siloBuilder.UseAzureStorageClustering(options =>
        {
            options.Configure<IServiceProvider>((opt, sp) =>
            {
                opt.TableServiceClient = sp.GetKeyedService<TableServiceClient>("DcbOrleansClusteringTable");
            });
        });
    }

    if (useInMemoryGrainStorage)
    {
        Console.WriteLine("[Orleans] Using in-memory grain storage");
        AddCompatibleMemoryGrainStorageAsDefault(siloBuilder);
        foreach (var store in new[] { "OrleansStorage", "dcb-orleans-queue", "DcbOrleansGrainTable", "EventStreamProvider", PubSubStoreName })
        {
            AddCompatibleMemoryGrainStorage(siloBuilder, store);
        }
        return;
    }

    // Configure Azure Storage grain storage
    siloBuilder.AddAzureBlobGrainStorageAsDefault(options =>
    {
        options.Configure<IServiceProvider>((opt, sp) =>
        {
            opt.BlobServiceClient = sp.GetKeyedService<BlobServiceClient>("DcbOrleansGrainState");
            opt.ContainerName = "sekiban-grainstate";
        });
    });

    void AddBlobStore(string name)
    {
        siloBuilder.AddAzureBlobGrainStorage(name, options =>
        {
            options.Configure<IServiceProvider>((opt, sp) =>
            {
                opt.BlobServiceClient = sp.GetKeyedService<BlobServiceClient>("DcbOrleansGrainState");
                opt.ContainerName = "sekiban-grainstate";
            });
        });
    }

    void AddTableStore(string name)
    {
        siloBuilder.AddAzureTableGrainStorage(name, options =>
        {
            options.Configure<IServiceProvider>((opt, sp) =>
            {
                opt.TableServiceClient = sp.GetKeyedService<TableServiceClient>("DcbOrleansGrainTable");
            });
        });
    }

    AddBlobStore("OrleansStorage");
    AddBlobStore("dcb-orleans-queue");
    AddTableStore("DcbOrleansGrainTable");
    AddTableStore("EventStreamProvider");

    // PubSubStore is registered conditionally based on stream type
    // (in-memory streams use MemoryGrainStorage, others use Azure Table)
    var useInMemoryStreams = configuration.GetValue<bool>(InMemoryStreamsConfigKey);
    if (!useInMemoryStreams)
    {
        AddTableStore(PubSubStoreName);
    }
}

void ConfigureStreaming(ISiloBuilder siloBuilder, IConfiguration configuration, string queueType)
{
    var useInMemoryStreams = configuration.GetValue<bool>(InMemoryStreamsConfigKey);

    if (useInMemoryStreams)
    {
        ConfigureInMemoryStreams(siloBuilder);
    }
    else if (queueType == "eventhub")
    {
        ConfigureEventHubStreams(siloBuilder, configuration);
    }
    else
    {
        ConfigureAzureQueueStreams(siloBuilder);
    }
}

void ConfigureInMemoryStreams(ISiloBuilder siloBuilder)
{
    Console.WriteLine("[Orleans] Using in-memory streams");

    void AddMemoryStream(string name)
    {
        siloBuilder.AddMemoryStreams(name, configurator =>
        {
            configurator.ConfigurePartitioning(8);
            configurator.ConfigurePullingAgent(options =>
            {
                options.Configure(opt =>
                {
                    opt.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(100);
                    opt.BatchContainerBatchSize = 100;
                });
            });
        });
    }

    AddMemoryStream("EventStreamProvider");
    AddMemoryStream("DcbOrleansQueue");
    AddCompatibleMemoryGrainStorage(siloBuilder, PubSubStoreName);
}

void ConfigureEventHubStreams(ISiloBuilder siloBuilder, IConfiguration configuration)
{
    Console.WriteLine("[Orleans] Using Event Hub streams");
    var eventHubConnection = configuration.GetConnectionString("OrleansEventHub");
    var eventHubName = configuration["ORLEANS_QUEUE_EVENTHUB_NAME"];
    var tableConnection = configuration.GetConnectionString("DcbOrleansGrainTable");

    void AddEventHubStream(string name, string checkpointTable)
    {
        siloBuilder.AddEventHubStreams(name, configurator =>
        {
            configurator.ConfigureEventHub(ob => ob.Configure(options =>
            {
                options.ConfigureEventHubConnection(eventHubConnection, eventHubName, "$Default");
            }));
            configurator.UseAzureTableCheckpointer(ob => ob.Configure(cp =>
            {
                cp.TableName = checkpointTable;
                cp.PersistInterval = TimeSpan.FromSeconds(10);
                cp.TableServiceClient = new TableServiceClient(tableConnection);
            }));
        });
    }

    AddEventHubStream("EventStreamProvider", "EventHubCheckpointsEventStreamsProvider");
    AddEventHubStream("DcbOrleansQueue", "EventHubCheckpointsOrleansSekibanQueue");
}

void ConfigureAzureQueueStreams(ISiloBuilder siloBuilder)
{
    Console.WriteLine("[Orleans] Using Azure Queue streams");

    void AddQueueStream(string name, List<string> queueNames)
    {
        siloBuilder.AddAzureQueueStreams(name, configurator =>
        {
            configurator.ConfigureAzureQueue(options =>
            {
                options.Configure<IServiceProvider>((queueOptions, sp) =>
                {
                    queueOptions.QueueServiceClient = sp.GetKeyedService<QueueServiceClient>("DcbOrleansQueue");
                    queueOptions.QueueNames = queueNames;
                    queueOptions.MessageVisibilityTimeout = TimeSpan.FromMinutes(2);
                });
            });
            configurator.Configure<HashRingStreamQueueMapperOptions>(ob => ob.Configure(o => o.TotalQueueCount = 3));
            configurator.ConfigurePullingAgent(ob => ob.Configure(opt =>
            {
                opt.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(1000);
                opt.BatchContainerBatchSize = 256;
                opt.StreamInactivityPeriod = TimeSpan.FromMinutes(10);
            }));
            configurator.ConfigureCacheSize(8192);
        });
    }

    AddQueueStream("EventStreamProvider", ["dcborleans-eventstreamprovider-0", "dcborleans-eventstreamprovider-1", "dcborleans-eventstreamprovider-2"]);
    AddQueueStream("DcbOrleansQueue", ["dcborleans-queue-0", "dcborleans-queue-1", "dcborleans-queue-2"]);
}

void ConfigureGrainStorage(ISiloBuilder siloBuilder, IConfiguration configuration)
{
    // Grain storage is configured in ConfigureCosmosOrleans or ConfigureLocalOrleans
    // This function is kept for future extensibility
}

void AddCompatibleMemoryGrainStorage(ISiloBuilder siloBuilder, string providerName)
{
    siloBuilder.ConfigureServices(services =>
    {
        services.AddGrainStorage<CompatibleMemoryGrainStorage>(
            providerName,
            static (serviceProvider, name) => new CompatibleMemoryGrainStorage(
                MemoryGrainStorageFactory.Create(serviceProvider, name)));
    });
}

void AddCompatibleMemoryGrainStorageAsDefault(ISiloBuilder siloBuilder) =>
    AddCompatibleMemoryGrainStorage(siloBuilder, "Default");

void ConfigureOrleansServices(ISiloBuilder siloBuilder, IHostEnvironment environment, string? databaseType, IConfiguration configuration)
{
    siloBuilder.ConfigureServices(services =>
    {
        services.AddTransient<IGrainStorageSerializer, NewtonsoftJsonDcbOrleansSerializer>();

        if (environment.IsDevelopment())
        {
            services.AddTransient<Sekiban.Dcb.MultiProjections.IMultiProjectionEventStatistics,
                Sekiban.Dcb.MultiProjections.RecordingMultiProjectionEventStatistics>();
        }
        else
        {
            services.AddTransient<Sekiban.Dcb.MultiProjections.IMultiProjectionEventStatistics,
                Sekiban.Dcb.MultiProjections.NoOpMultiProjectionEventStatistics>();
        }

        var dynamicOptions = new GeneralMultiProjectionActorOptions
        {
            SafeWindowMs = databaseType == "sqlite" ? 5000 : 20000,
            EnableDynamicSafeWindow = databaseType != "sqlite" && !configuration.GetValue<bool>("Orleans:UseInMemoryStreams"),
            MaxExtraSafeWindowMs = 30000,
            LagEmaAlpha = 0.3,
            LagDecayPerSecond = 0.98
        };
        services.AddTransient(_ => dynamicOptions);
    });
}

void ConfigureEndpoints(ISiloBuilder siloBuilder, IHostEnvironment environment)
{
    // In development, Orleans uses localhost clustering which handles endpoints automatically
    if (environment.IsDevelopment())
    {
        Console.WriteLine("[Orleans] Development mode - using default endpoint configuration");
        return;
    }

    // For Azure Container Apps, configure explicit endpoints
    // These ports must match the additionalPortMappings in the Bicep configuration
    const int siloPort = 11111;
    const int gatewayPort = 30000;
    var advertisedIp = GetPrivateIpAddress();

    Console.WriteLine($"[Orleans] Configuring endpoints - SiloPort: {siloPort}, GatewayPort: {gatewayPort}, AdvertisedIP: {advertisedIp}");

    siloBuilder.Configure<Orleans.Configuration.EndpointOptions>(options =>
    {
        options.SiloPort = siloPort;
        options.GatewayPort = gatewayPort;
        options.AdvertisedIPAddress = advertisedIp;
        // Listen on all interfaces
        options.SiloListeningEndpoint = new IPEndPoint(IPAddress.Any, siloPort);
        options.GatewayListeningEndpoint = new IPEndPoint(IPAddress.Any, gatewayPort);
    });
}

// Helper method to get private IP address for Azure Container Apps
static IPAddress GetPrivateIpAddress()
{
    // Get the first non-loopback IPv4 address
    var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
    foreach (var ip in host.AddressList)
    {
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
            !IPAddress.IsLoopback(ip))
        {
            return ip;
        }
    }

    // Last resort: return loopback
    return IPAddress.Loopback;
}

static async Task EnsurePostgresDatabaseExistsAsync(string? connectionString)
{
    if (!TryCreatePostgresBootstrapSettings(connectionString, out var settings))
    {
        return;
    }

    Exception? lastException = null;

    for (var attempt = 1; attempt <= MaxEnsureDatabaseAttempts; attempt++)
    {
        try
        {
            await EnsurePostgresDatabaseExistsOnceAsync(settings);
            return;
        }
        catch (PostgresException ex) when (ex.SqlState == DuplicateDatabaseSqlState)
        {
            return;
        }
        catch (PostgresException ex) when (ex.SqlState == InsufficientPrivilegeSqlState)
        {
            throw new InvalidOperationException(
                $"PostgreSQL user cannot create database '{settings.DatabaseName}'. Disable '{EnsurePostgresDatabaseExistsConfigKey}' or pre-create the database when using managed PostgreSQL credentials without CREATEDB.",
                ex);
        }
        catch (NpgsqlException ex) when (attempt < MaxEnsureDatabaseAttempts)
        {
            lastException = ex;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) when (attempt < MaxEnsureDatabaseAttempts)
        {
            lastException = ex;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    throw new InvalidOperationException(
        $"Failed to ensure PostgreSQL database '{settings.DatabaseName}' exists.",
        lastException);
}

static async Task EnsurePostgresDatabaseExistsOnceAsync(PostgresBootstrapSettings settings)
{
    await using var connection = new NpgsqlConnection(settings.AdminConnectionString);
    await connection.OpenAsync();

    await using var existsCommand = new NpgsqlCommand(
        "SELECT 1 FROM pg_database WHERE datname = @databaseName;",
        connection);
    existsCommand.Parameters.AddWithValue("databaseName", settings.DatabaseName);
    var exists = await existsCommand.ExecuteScalarAsync();
    if (exists is not null)
    {
        return;
    }

    await using var createCommand = new NpgsqlCommand(
        $"CREATE DATABASE \"{settings.EscapedDatabaseName}\";",
        connection);
    await createCommand.ExecuteNonQueryAsync();
}

static bool TryCreatePostgresBootstrapSettings(string? connectionString, out PostgresBootstrapSettings settings)
{
    settings = default;
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return false;
    }

    var targetBuilder = new NpgsqlConnectionStringBuilder(connectionString);
    if (string.IsNullOrWhiteSpace(targetBuilder.Database))
    {
        return false;
    }

    settings = new PostgresBootstrapSettings(
        targetBuilder.Database,
        targetBuilder.Database.Replace("\"", "\"\""),
        new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = PostgresAdminDatabaseName,
            Pooling = false
        }.ConnectionString);
    return true;
}
