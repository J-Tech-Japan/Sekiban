using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Dcb.Domain.Decider;
using DcbOrleans.WithoutResult.ApiService.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Orleans.Configuration;
using Microsoft.Extensions.Logging;
using Orleans.Storage;
using Scalar.AspNetCore;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.BlobStorage.AzureStorage;
using SekibanDcbOrleans.ApiService.Endpoints;

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



// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add Azure Storage clients for Orleans
builder.AddKeyedAzureTableServiceClient("DcbOrleansClusteringTable");
builder.AddKeyedAzureTableServiceClient("DcbOrleansGrainTable");
builder.AddKeyedAzureBlobServiceClient("DcbOrleansGrainState");
builder.AddKeyedAzureBlobServiceClient("MultiProjectionOffload");
builder.AddKeyedAzureQueueServiceClient("DcbOrleansQueue");

// Add Cosmos DB client for Orleans (if using Cosmos)
if ((builder.Configuration["ORLEANS_CLUSTERING_TYPE"] ?? "").ToLower() == "cosmos" ||
    (builder.Configuration["ORLEANS_GRAIN_DEFAULT_TYPE"] ?? "").ToLower() == "cosmos")
{
    builder.AddAzureCosmosClient("OrleansCosmos");
}

var cfgGrainDefault = builder.Configuration["ORLEANS_GRAIN_DEFAULT_TYPE"]?.ToLower() ?? "blob";

// Configure Orleans
builder.UseOrleans(config =>
{
    if (builder.Environment.IsDevelopment())
    {
        config.UseLocalhostClustering();
    }
    else
    {
        if ((builder.Configuration["ORLEANS_CLUSTERING_TYPE"] ?? "").ToLower() == "cosmos")
        {
            var connectionString = builder.Configuration.GetConnectionString("OrleansCosmos") ??
                                   throw new InvalidOperationException();
            config.UseCosmosClustering(options =>
            {
                options.ConfigureCosmosClient(connectionString);
                // this can be enabled if you use Provisioning
                // options.IsResourceCreationEnabled = true;
            });
        }
    }

    // Check if we should use in-memory streams (for development/testing)
    var useInMemoryStreams = builder.Configuration.GetValue<bool>("Orleans:UseInMemoryStreams");

    if (useInMemoryStreams)
    {
        // Use in-memory streams for development/testing with enhanced configuration
        config.AddMemoryStreams("EventStreamProvider", configurator =>
        {
            // Increase partitions for better parallelism
            configurator.ConfigurePartitioning(8);

            // Configure pulling agent for better batch processing
            configurator.ConfigurePullingAgent(options =>
            {
                options.Configure(opt =>
                {
                    // Process events more frequently
                    opt.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(100);
                    // Increase batch size for better throughput
                    opt.BatchContainerBatchSize = 100;
                });
            });
        });

        config.AddMemoryStreams("DcbOrleansQueue", configurator =>
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

        // Add memory storage for PubSubStore when using in-memory streams
        config.AddMemoryGrainStorage("PubSubStore");
    }
    else
    {
        // Add Azure Queue Streams for production
        config.AddAzureQueueStreams(
            "EventStreamProvider",
            configurator =>
            {
                configurator.ConfigureAzureQueue(options =>
                {
                    options.Configure<IServiceProvider>((queueOptions, sp) =>
                    {
                        queueOptions.QueueServiceClient = sp.GetKeyedService<QueueServiceClient>("DcbOrleansQueue");
                        queueOptions.QueueNames =
                        [
                            "dcborleans-eventstreamprovider-0",
                            "dcborleans-eventstreamprovider-1",
                            "dcborleans-eventstreamprovider-2"
                        ];
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

        config.AddAzureQueueStreams(
            "DcbOrleansQueue",
            configurator =>
            {
                configurator.ConfigureAzureQueue(options =>
                {
                    options.Configure<IServiceProvider>((queueOptions, sp) =>
                    {
                        queueOptions.QueueServiceClient = sp.GetKeyedService<QueueServiceClient>("DcbOrleansQueue");
                        queueOptions.QueueNames =
                        [
                            "dcborleans-queue-0",
                            "dcborleans-queue-1",
                            "dcborleans-queue-2"
                        ];
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

    // Configure grain storage providers
    Console.WriteLine($"UseOrleans: ORLEANS_GRAIN_DEFAULT_TYPE={cfgGrainDefault}");
    if (cfgGrainDefault == "cosmos")
    {
        config.AddCosmosGrainStorageAsDefault(options =>
        {
            var connectionString = builder.Configuration.GetConnectionString("OrleansCosmos") ??
                                   throw new InvalidOperationException();
            options.ConfigureCosmosClient(connectionString);
            options.IsResourceCreationEnabled = true;
        });
    }
    else
    {
        // Default storage using Azure Blob Storage
        config.AddAzureBlobGrainStorageAsDefault(options =>
        {
            options.Configure<IServiceProvider>((opt, sp) =>
            {
                opt.BlobServiceClient = sp.GetKeyedService<BlobServiceClient>("DcbOrleansGrainState");
                opt.ContainerName = "sekiban-grainstate"; // 明示コンテナ
            });
        });
    }

    // OrleansStorage provider for MultiProjectionGrain
    if (cfgGrainDefault == "cosmos")
    {
        config.AddCosmosGrainStorage(
            "OrleansStorage",
            options =>
            {
                var connectionString = builder.Configuration.GetConnectionString("OrleansCosmos") ??
                                       throw new InvalidOperationException();
                options.ConfigureCosmosClient(connectionString);
                options.IsResourceCreationEnabled = true;
            });
    }
    else
    {
        config.AddAzureBlobGrainStorage(
            "OrleansStorage",
            options =>
            {
                options.Configure<IServiceProvider>((opt, sp) =>
                {
                    opt.BlobServiceClient = sp.GetKeyedService<BlobServiceClient>("DcbOrleansGrainState");
                    opt.ContainerName = "sekiban-grainstate";
                });
            });
    }

    // Additional named storage providers
    if (cfgGrainDefault == "cosmos")
    {
        config.AddCosmosGrainStorage(
            "dcb-orleans-queue",
            options =>
            {
                var connectionString = builder.Configuration.GetConnectionString("OrleansCosmos") ??
                                       throw new InvalidOperationException();
                options.ConfigureCosmosClient(connectionString);
                options.IsResourceCreationEnabled = true;
            });
    }
    else
    {
        config.AddAzureBlobGrainStorage(
            "dcb-orleans-queue",
            options =>
            {
                options.Configure<IServiceProvider>((opt, sp) =>
                {
                    opt.BlobServiceClient = sp.GetKeyedService<BlobServiceClient>("DcbOrleansGrainState");
                    opt.ContainerName = "sekiban-grainstate";
                });
            });
    }

    if (cfgGrainDefault == "cosmos")
    {
        config.AddCosmosGrainStorage(
            "DcbOrleansGrainTable",
            options =>
            {
                var connectionString = builder.Configuration.GetConnectionString("OrleansCosmos") ??
                                       throw new InvalidOperationException();
                options.ConfigureCosmosClient(connectionString);
                options.IsResourceCreationEnabled = true;
            });
    }
    else
    {
        config.AddAzureTableGrainStorage(
            "DcbOrleansGrainTable",
            options =>
            {
                options.Configure<IServiceProvider>((opt, sp) =>
                {
                    opt.TableServiceClient = sp.GetKeyedService<TableServiceClient>("DcbOrleansGrainTable");
                });
            });
    }

    // Add grain storage for PubSub (used by Orleans streaming) - only for Azure Queue Streams
    if (!useInMemoryStreams)
    {
        if (cfgGrainDefault == "cosmos")
        {
            config.AddCosmosGrainStorage(
                "PubSubStore",
                options =>
                {
                    var connectionString = builder.Configuration.GetConnectionString("OrleansCosmos") ??
                                           throw new InvalidOperationException();
                    options.ConfigureCosmosClient(connectionString);
                    options.IsResourceCreationEnabled = true;
                });
        }
        else
        {
            config.AddAzureTableGrainStorage(
                "PubSubStore",
                options =>
                {
                    options.Configure<IServiceProvider>((opt, sp) =>
                    {
                        opt.TableServiceClient = sp.GetKeyedService<TableServiceClient>("DcbOrleansGrainTable");
                        opt.GrainStorageSerializer = sp.GetRequiredService<NewtonsoftJsonDcbOrleansSerializer>();
                    });
                    options.Configure<IGrainStorageSerializer>((op, serializer) => op.GrainStorageSerializer = serializer);
                });
        }
    }

    // Add grain storage for the stream provider
    if (cfgGrainDefault == "cosmos")
    {
        config.AddCosmosGrainStorage(
            "EventStreamProvider",
            options =>
            {
                var connectionString = builder.Configuration.GetConnectionString("OrleansCosmos") ??
                                       throw new InvalidOperationException();
                options.ConfigureCosmosClient(connectionString);
                options.IsResourceCreationEnabled = true;
            });
    }
    else
    {
        config.AddAzureTableGrainStorage(
            "EventStreamProvider",
            options =>
            {
                options.Configure<IServiceProvider>((opt, sp) =>
                {
                    opt.TableServiceClient = sp.GetKeyedService<TableServiceClient>("DcbOrleansGrainTable");
                    opt.GrainStorageSerializer = sp.GetRequiredService<NewtonsoftJsonDcbOrleansSerializer>();
                });
                options.Configure<IGrainStorageSerializer>((op, serializer) => op.GrainStorageSerializer = serializer);
            });
    }

    // Orleans will automatically discover grains in the same assembly
    config.ConfigureServices(services =>
    {
        services.AddTransient<IGrainStorageSerializer, NewtonsoftJsonDcbOrleansSerializer>();
        // Event delivery statistics: enable detailed recording only in Development
        if (builder.Environment.IsDevelopment())
        {
            // Per-grain (per-activation) instance for stats to keep instances isolated
            services.AddTransient<Sekiban.Dcb.MultiProjections.IMultiProjectionEventStatistics, Sekiban.Dcb.MultiProjections.RecordingMultiProjectionEventStatistics>();
        }
        else
        {
            services.AddTransient<Sekiban.Dcb.MultiProjections.IMultiProjectionEventStatistics, Sekiban.Dcb.MultiProjections.NoOpMultiProjectionEventStatistics>();
        }

        // GeneralMultiProjectionActor options: enable dynamic safe window when not using in-memory streams
        // Default baseline uses 20s safe window, dynamic adds observed stream lag up to 30s.
        var dynamicOptions = new GeneralMultiProjectionActorOptions
        {
            SafeWindowMs = 20000,
            EnableDynamicSafeWindow = !builder.Configuration.GetValue<bool>("Orleans:UseInMemoryStreams"),
            MaxExtraSafeWindowMs = 30000,
            LagEmaAlpha = 0.3,
            LagDecayPerSecond = 0.98
        };
        // Per-activation scope is appropriate; Orleans constructs grains per activation
        services.AddTransient<GeneralMultiProjectionActorOptions>(_ => dynamicOptions);
    });

    // Orleans will automatically discover and use the EventSurrogate
});

var domainTypes = DomainType.GetDomainTypes();
builder.Services.AddSingleton(domainTypes);

// Configure database storage based on configuration
var databaseType = builder.Configuration.GetSection("Sekiban").GetValue<string>("Database")?.ToLower();
if (databaseType == "cosmos")
{
    // CosmosDB settings - Aspire will automatically provide CosmosClient if configured
    builder.Services.AddSekibanDcbCosmosDbWithAspire();
    builder.Services.AddSingleton<IMultiProjectionStateStore, Sekiban.Dcb.CosmosDb.CosmosMultiProjectionStateStore>();
}
else
{
    // Postgres settings (default)
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
// Snapshot offload: Azure Blob Storage accessor using dedicated Aspire-configured BlobServiceClient
builder.Services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
{
    var blobServiceClient = sp.GetRequiredKeyedService<BlobServiceClient>("MultiProjectionOffload");
    var containerName = "multiprojection-snapshot-offload";
    return new AzureBlobStorageSnapshotAccessor(blobServiceClient, containerName);
});
// Note: IEventSubscription is now created per-grain via IEventSubscriptionResolver
builder.Services.AddTransient<ISekibanExecutor, OrleansDcbExecutor>();
builder.Services.AddTransient<ISekibanExecutor, OrleansDcbExecutor>();
builder.Services.AddScoped<IActorObjectAccessor, OrleansActorObjectAccessor>();

// Note: TagStatePersistent is not needed when using Orleans as Orleans grains have their own persistence
// IEventStoreとDbContextFactoryは既にAddSekibanDcbPostgresWithAspireで登録済み

if (builder.Environment.IsDevelopment())
{
    // Add CORS services
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            // Allow any origin in development for easier testing
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        });
    });
}
var app = builder.Build();

// Database tables will be created automatically by the DatabaseInitializerService
// configured in AddSekibanDcbPostgresWithAspire, so no need to run migrations

var apiRoute = app.MapGroup("/api");

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Use CORS middleware
app.UseCors();

// Map all endpoints
apiRoute.MapStudentEndpoints();
apiRoute.MapClassRoomEndpoints();
apiRoute.MapEnrollmentEndpoints();
apiRoute.MapWeatherEndpoints();
apiRoute.MapProjectionEndpoints();
apiRoute.MapDebugEndpoints();

app.MapDefaultEndpoints();

app.Run();
