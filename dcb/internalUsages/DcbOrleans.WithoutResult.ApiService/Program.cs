using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using System.Text;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.ClassRoom;
using Dcb.Domain.WithoutResult.Enrollment;
using Dcb.Domain.WithoutResult.MaterializedViews;
using Dcb.Domain.WithoutResult.Order;
using Dcb.Domain.WithoutResult.Queries;
using Dcb.Domain.WithoutResult.Student;
using Dcb.Domain.WithoutResult.Weather;
using DcbOrleans.WithoutResult.ApiService;
using DcbOrleans.WithoutResult.ApiService.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Orleans.Configuration;
using Microsoft.Extensions.Logging;
using Orleans.Storage;
using Scalar.AspNetCore;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.BlobStorage.AzureStorage;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Orleans;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.ServiceId;
using Npgsql;
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
builder.AddKeyedAzureTableClient("DcbOrleansClusteringTable");
builder.AddKeyedAzureTableClient("DcbOrleansGrainTable");
builder.AddKeyedAzureBlobClient("DcbOrleansGrainState");
builder.AddKeyedAzureBlobClient("MultiProjectionOffload");
builder.AddKeyedAzureQueueClient("DcbOrleansQueue");

// Add Cosmos DB client for Orleans (if using Cosmos)
if ((builder.Configuration["ORLEANS_CLUSTERING_TYPE"] ?? "").ToLower() == "cosmos" ||
    (builder.Configuration["ORLEANS_GRAIN_DEFAULT_TYPE"] ?? "").ToLower() == "cosmos")
{
    builder.AddAzureCosmosClient("OrleansCosmos");
}

var cfgGrainDefault = builder.Configuration["ORLEANS_GRAIN_DEFAULT_TYPE"]?.ToLower() ?? "blob";
var databaseType = builder.Configuration.GetSection("Sekiban").GetValue<string>("Database")?.ToLower();

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
        // SQLite uses 5s safe window, others use 20s baseline. Dynamic adds observed stream lag up to 30s.
        var dynamicOptions = new GeneralMultiProjectionActorOptions
        {
            SafeWindowMs = databaseType == "sqlite" ? 5000 : 20000,
            EnableDynamicSafeWindow = databaseType != "sqlite" && !builder.Configuration.GetValue<bool>("Orleans:UseInMemoryStreams"),
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

// Register native runtime abstraction interfaces
builder.Services.AddSekibanDcbNativeRuntime();
builder.Services.AddSekibanDcbColdEventDefaults();
builder.Services.AddSekibanDcbMaterializedView(options =>
{
    options.BatchSize = 100;
    options.PollInterval = TimeSpan.FromSeconds(1);
});
builder.Services.AddMaterializedView<OrderSummaryMvV1>();
builder.Services.AddMaterializedView<WeatherForecastMvV1>();

// Configure database storage based on configuration
if (databaseType == "cosmos")
{
    // CosmosDB settings - Aspire will automatically provide CosmosClient if configured
    builder.Services.AddSekibanDcbCosmosDbWithAspire();
    builder.Services.AddSingleton<IMultiProjectionStateStore, Sekiban.Dcb.CosmosDb.CosmosMultiProjectionStateStore>();
}
else if (databaseType == "sqlite")
{
    // SQLite settings - use local events.db file
    // Prefer project directory for development, fallback to base directory
    var projectPath = Path.Combine(Directory.GetCurrentDirectory(), "events.db");
    var sqlitePath = projectPath;
    Console.WriteLine($"Using SQLite database: {sqlitePath}");
    // SqliteEventStore will auto-create the database if AutoCreateDatabase is true (default)
    builder.Services.AddSekibanDcbSqlite(sqlitePath);
}
else
{
    // Postgres settings (default)
    builder.Services.AddSingleton<Sekiban.Dcb.ServiceId.IServiceIdProvider, Sekiban.Dcb.ServiceId.DefaultServiceIdProvider>();
    builder.Services.AddSingleton<IEventStore, PostgresEventStore>();
    builder.Services.AddSekibanDcbPostgresWithAspire();
    builder.Services.AddSekibanDcbMaterializedViewPostgres(
        builder.Configuration,
        connectionStringName: "DcbMaterializedViewPostgres",
        registerHostedWorker: false);
    builder.Services.AddSekibanDcbMaterializedViewOrleans();
    builder.Services.AddSingleton<IMultiProjectionStateStore, Sekiban.Dcb.Postgres.PostgresMultiProjectionStateStore>();
}

if (builder.Configuration.GetSection("Sekiban:ColdEvent").GetValue<bool>("Enabled"))
{
    var coldConfig = builder.Configuration.GetSection("Sekiban:ColdEvent");
    var storageOptions = coldConfig.GetSection("Storage").Get<ColdStorageOptions>() ?? new ColdStorageOptions();
    var storageRoot = ColdObjectStorageFactory.ResolveStorageRoot(storageOptions, Directory.GetCurrentDirectory());
    builder.Services.AddSingleton(storageOptions);
    builder.Services.AddSingleton<IColdObjectStorage>(sp =>
        ColdObjectStorageFactory.Create(storageOptions, storageRoot, sp));
    builder.Services.AddSingleton<IColdLeaseManager, StorageBackedColdLeaseManager>();
    builder.Services.AddSekibanDcbColdEvents(options => coldConfig.Bind(options));
    builder.Services.AddSekibanDcbColdEventHybridRead();
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

// Student endpoints
apiRoute
    .MapPost(
        "/orders",
        async ([FromBody] CreateOrder command, [FromServices] ISekibanExecutor executor) =>
        {
            var execution = await executor.ExecuteAsync(command);
            var createdEvent = execution.Events.FirstOrDefault(item => item.Payload is OrderCreated)?.Payload as OrderCreated;
            return Results.Ok(
                new
                {
                    orderId = createdEvent?.OrderId ?? command.OrderId,
                    eventId = execution.EventId,
                    sortableUniqueId = execution.SortableUniqueId,
                    message = "Order created successfully"
                });
        })
    .WithOpenApi()
    .WithName("CreateOrder");

apiRoute
    .MapPost(
        "/orders/{orderId:guid}/items",
        async (Guid orderId, [FromBody] AddOrderItem command, [FromServices] ISekibanExecutor executor) =>
        {
            var execution = await executor.ExecuteAsync(command with { OrderId = orderId });
            var addedEvent = execution.Events.FirstOrDefault(item => item.Payload is OrderItemAdded)?.Payload as OrderItemAdded;
            return Results.Ok(
                new
                {
                    orderId,
                    itemId = addedEvent?.ItemId ?? command.ItemId,
                    eventId = execution.EventId,
                    sortableUniqueId = execution.SortableUniqueId,
                    message = "Order item added successfully"
                });
        })
    .WithOpenApi()
    .WithName("AddOrderItem");

apiRoute
    .MapPost(
        "/orders/{orderId:guid}/cancel",
        async (Guid orderId, [FromBody] CancelOrder command, [FromServices] ISekibanExecutor executor) =>
        {
            var execution = await executor.ExecuteAsync(command with { OrderId = orderId });
            return Results.Ok(
                new
                {
                    orderId,
                    eventId = execution.EventId,
                    sortableUniqueId = execution.SortableUniqueId,
                    message = "Order cancelled successfully"
                });
        })
    .WithOpenApi()
    .WithName("CancelOrder");

apiRoute
    .MapPost(
        "/students",
        async ([FromBody] CreateStudent command, [FromServices] ISekibanExecutor executor) =>
        {
            var execution = await executor.ExecuteAsync(command);
            return Results.Ok(
                new
                {
                    studentId = command.StudentId,
                    eventId = execution.EventId,
                    sortableUniqueId = execution.SortableUniqueId,
                    message = "Student created successfully"
                });
        })
    .WithOpenApi()
    .WithName("CreateStudent");

apiRoute
    .MapGet(
        "/students",
        async (
            [FromServices] ISekibanExecutor executor,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize,
            [FromQuery] string? waitForSortableUniqueId) =>
        {
            var query = new GetStudentListQuery
            {
                PageNumber = pageNumber ?? 1,
                PageSize = pageSize ?? 20,
                WaitForSortableUniqueId = waitForSortableUniqueId
            };
            var result = await executor.QueryAsync(query);
            return Results.Ok(result.Items);
        })
    .WithOpenApi()
    .WithName("GetStudentList");

apiRoute
    .MapGet(
        "/students/{studentId:guid}",
        async (Guid studentId, [FromServices] ISekibanExecutor executor) =>
        {
            var tag = new StudentTag(studentId);
            var state = await executor.GetTagStateAsync(new TagStateId(tag, nameof(StudentProjector)));

            return Results.Ok(
                new
                {
                    studentId,
                    payload = state.Payload as dynamic,
                    version = state.Version
                });
        })
    .WithOpenApi()
    .WithName("GetStudent");

// ClassRoom endpoints
apiRoute
    .MapPost(
        "/classrooms",
        async ([FromBody] CreateClassRoom command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command, CreateClassRoomHandler.HandleAsync);
            return Results.Ok(
                new
                {
                    classRoomId = command.ClassRoomId,
                    eventId = result.EventId,
                    sortableUniqueId = result.SortableUniqueId,
                    message = "ClassRoom created successfully"
                });
        })
    .WithOpenApi()
    .WithName("CreateClassRoom");

apiRoute
    .MapGet(
        "/classrooms",
        async (
            [FromServices] ISekibanExecutor executor,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize,
            [FromQuery] string? waitForSortableUniqueId) =>
        {
            var query = new GetClassRoomListQuery
            {
                PageNumber = pageNumber ?? 1,
                PageSize = pageSize ?? 20,
                WaitForSortableUniqueId = waitForSortableUniqueId
            };
            var result = await executor.QueryAsync(query);
            return Results.Ok(result.Items);
        })
    .WithOpenApi()
    .WithName("GetClassRoomList");

apiRoute
    .MapGet(
        "/classrooms/{classRoomId:guid}",
        async (Guid classRoomId, [FromServices] ISekibanExecutor executor) =>
        {
            var tag = new ClassRoomTag(classRoomId);
            var state = await executor.GetTagStateAsync(new TagStateId(tag, nameof(ClassRoomProjector)));

            return Results.Ok(
                new
                {
                    classRoomId,
                    payload = state.Payload,
                    version = state.Version
                });
        })
    .WithOpenApi()
    .WithName("GetClassRoom");

// Enrollment endpoints
apiRoute
    .MapPost(
        "/enrollments/add",
        async ([FromBody] EnrollStudentInClassRoom command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command, EnrollStudentInClassRoomHandler.HandleAsync);
            return Results.Ok(
                new
                {
                    studentId = command.StudentId,
                    classRoomId = command.ClassRoomId,
                    eventId = result.EventId,
                    sortableUniqueId = result.SortableUniqueId,
                    message = "Student enrolled successfully"
                });
        })
    .WithOpenApi()
    .WithName("EnrollStudent");

apiRoute
    .MapPost(
        "/enrollments/drop",
        async ([FromBody] DropStudentFromClassRoom command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command, DropStudentFromClassRoomHandler.HandleAsync);
            return Results.Ok(
                new
                {
                    studentId = command.StudentId,
                    classRoomId = command.ClassRoomId,
                    eventId = result.EventId,
                    sortableUniqueId = result.SortableUniqueId,
                    message = "Student dropped successfully"
                });
        })
    .WithOpenApi()
    .WithName("DropStudent");

// Debug endpoint to check database
apiRoute
    .MapGet(
        "/debug/events",
        async ([FromServices] IEventStore eventStore) =>
        {
            var result = await eventStore.ReadAllSerializableEventsAsync();
            var events = result.GetValue().ToList();
            Console.WriteLine($"[Debug] ReadAllSerializableEventsAsync returned {events.Count} events");
            return Results.Ok(
                new
                {
                    totalEvents = events.Count,
                    events = events.Select(e => new
                    {
                        id = e.Id,
                        type = e.EventPayloadName,
                        sortableId = e.SortableUniqueIdValue,
                        tags = e.Tags
                    })
                });
        })
    .WithOpenApi()
    .WithName("DebugGetEvents");

// Weather endpoints
apiRoute
    .MapGet(
        "/weatherforecast",
        async (
            [FromQuery] string? waitForSortableUniqueId,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize,
            [FromServices] ISekibanExecutor executor) =>
        {
            pageNumber ??= 1;
            pageSize ??= 100;
            var query = new GetWeatherForecastListQuery
            {
                WaitForSortableUniqueId = waitForSortableUniqueId,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
            var result = await executor.QueryAsync(query);
            return Results.Ok(result.Items);
        })
    .WithOpenApi()
    .WithName("GetWeatherForecast");

// Weather endpoints (GenericTagMultiProjector)
apiRoute
    .MapGet(
        "/weatherforecastgeneric",
        async (
            [FromQuery] string? waitForSortableUniqueId,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize,
            [FromServices] ISekibanExecutor executor) =>
        {
            pageNumber ??= 1;
            pageSize ??= 100;
            var query = new GetWeatherForecastListGenericQuery
            {
                WaitForSortableUniqueId = waitForSortableUniqueId,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
            var result = await executor.QueryAsync(query);
            return Results.Ok(result.Items);
        })
    .WithOpenApi()
    .WithName("GetWeatherForecastGeneric");

// Weather endpoints (Single projector with SafeUnsafeProjectionState)
apiRoute
    .MapGet(
        "/weatherforecastsingle",
        async (
            [FromQuery] string? waitForSortableUniqueId,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize,
            [FromServices] ISekibanExecutor executor) =>
        {
            pageNumber ??= 1;
            pageSize ??= 100;
            var query = new GetWeatherForecastListSingleQuery
            {
                WaitForSortableUniqueId = waitForSortableUniqueId,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
            var result = await executor.QueryAsync(query);
            return Results.Ok(result.Items);
        })
    .WithOpenApi()
    .WithName("GetWeatherForecastSingle");

apiRoute
    .MapGet(
        "/weatherforecastdb",
        async (
            [FromQuery] string? waitForSortableUniqueId,
            [FromQuery] bool? includeDeleted,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize,
            [FromServices] IMvOrleansQueryAccessor mvQueryAccessor,
            [FromServices] WeatherForecastMvV1 projector) =>
        {
            if (!string.Equals(databaseType, "postgres", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(databaseType))
            {
                return Results.BadRequest(
                    new
                    {
                        message = "Weather DB projection endpoint is only available with PostgreSQL storage.",
                        databaseType
                    });
            }

            var context = await mvQueryAccessor.GetAsync(projector);

            if (!string.IsNullOrWhiteSpace(waitForSortableUniqueId))
            {
                var reached = await WaitForMaterializedViewAsync(context.Grain, waitForSortableUniqueId, TimeSpan.FromSeconds(10));
                if (!reached)
                {
                    return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
                }
            }

            var forecastEntry = context.GetRequiredTable(WeatherForecastDbProjection.LogicalTable);
            await using var connection = new NpgsqlConnection(context.ConnectionString);
            await connection.OpenAsync();

            var rows = await QueryWeatherForecastDbRowsAsync(
                connection,
                context,
                includeDeleted.GetValueOrDefault(),
                pageNumber,
                pageSize);

            var status = await context.Grain.GetStatusAsync();
            return Results.Ok(
                new
                {
                    status,
                    databaseType = context.DatabaseType,
                    entries = context.Entries,
                    table = forecastEntry.PhysicalTable,
                    rows
                });
        })
    .WithOpenApi()
    .WithName("GetWeatherForecastDb");

apiRoute
    .MapGet(
        "/weatherforecastdb/list",
        async (
            [FromQuery] string? waitForSortableUniqueId,
            [FromQuery] bool? includeDeleted,
            [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize,
            [FromServices] IMvOrleansQueryAccessor mvQueryAccessor,
            [FromServices] WeatherForecastMvV1 projector) =>
        {
            if (!string.Equals(databaseType, "postgres", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(databaseType))
            {
                return Results.BadRequest(
                    new
                    {
                        message = "Weather DB projection endpoint is only available with PostgreSQL storage.",
                        databaseType
                    });
            }

            var context = await mvQueryAccessor.GetAsync(projector);

            if (!string.IsNullOrWhiteSpace(waitForSortableUniqueId))
            {
                var reached = await WaitForMaterializedViewAsync(context.Grain, waitForSortableUniqueId, TimeSpan.FromSeconds(10));
                if (!reached)
                {
                    return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
                }
            }

            await using var connection = new NpgsqlConnection(context.ConnectionString);
            await connection.OpenAsync();

            var rows = await QueryWeatherForecastDbRowsAsync(
                connection,
                context,
                includeDeleted.GetValueOrDefault(),
                pageNumber,
                pageSize);

            var items = rows
                .Select(
                    row => new Dcb.Domain.WithoutResult.Projections.WeatherForecastItem(
                        row.ForecastId,
                        row.Location,
                        DateTime.SpecifyKind(row.ForecastDate, DateTimeKind.Utc),
                        row.TemperatureC,
                        row.Summary,
                        row.LastAppliedAt.UtcDateTime))
                .ToList();

            return Results.Ok(items);
        })
    .WithOpenApi()
    .WithName("GetWeatherForecastDbList");

apiRoute
    .MapGet(
        "/weatherforecastdb/count",
        async (
            [FromServices] IMvOrleansQueryAccessor mvQueryAccessor,
            [FromServices] WeatherForecastMvV1 projector) =>
        {
            try
            {
                var context = await mvQueryAccessor.GetAsync(projector);
                var forecastEntry = context.GetRequiredTable(WeatherForecastDbProjection.LogicalTable);

                await using var connection = new NpgsqlConnection(context.ConnectionString);
                await connection.OpenAsync();
                var totalCount = await connection.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(*) FROM {forecastEntry.PhysicalTable} WHERE is_deleted = FALSE;");
                var status = await context.Grain.GetStatusAsync();

                return Results.Ok(
                    new
                    {
                        totalCount,
                        databaseType = context.DatabaseType,
                        table = forecastEntry.PhysicalTable,
                        status,
                        entry = forecastEntry,
                        entries = context.Entries
                    });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        })
    .WithOpenApi()
    .WithName("GetWeatherForecastDbCount");

apiRoute
    .MapGet(
        "/weatherforecastdb/status",
        async (
            [FromServices] IMvOrleansQueryAccessor mvQueryAccessor,
            [FromServices] WeatherForecastMvV1 projector) =>
        {
            try
            {
                var context = await mvQueryAccessor.GetAsync(projector);
                var forecastEntry = context.GetRequiredTable(WeatherForecastDbProjection.LogicalTable);
                var status = await context.Grain.GetStatusAsync();
                return Results.Ok(new { databaseType = context.DatabaseType, table = forecastEntry.PhysicalTable, status, entry = forecastEntry, entries = context.Entries });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        })
    .WithOpenApi()
    .WithName("GetWeatherForecastDbStatus");

apiRoute
    .MapPost(
        "/weatherforecastdb/refresh",
        async (
            [FromServices] IMvOrleansQueryAccessor mvQueryAccessor,
            [FromServices] WeatherForecastMvV1 projector) =>
        {
            try
            {
                var context = await mvQueryAccessor.GetAsync(projector);
                var forecastEntry = context.GetRequiredTable(WeatherForecastDbProjection.LogicalTable);
                await context.Grain.RefreshAsync();
                return Results.Ok(new { success = true, databaseType = context.DatabaseType, table = forecastEntry.PhysicalTable });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        })
    .WithOpenApi()
    .WithName("RefreshWeatherForecastDb");

apiRoute
    .MapPost(
        "/weatherforecastdb/deactivate",
        async (
            [FromServices] IMvOrleansQueryAccessor mvQueryAccessor,
            [FromServices] WeatherForecastMvV1 projector) =>
        {
            try
            {
                var context = await mvQueryAccessor.GetAsync(projector);
                var forecastEntry = context.GetRequiredTable(WeatherForecastDbProjection.LogicalTable);
                await context.Grain.RequestDeactivationAsync();
                return Results.Ok(new { success = true, databaseType = context.DatabaseType, table = forecastEntry.PhysicalTable });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        })
    .WithOpenApi()
    .WithName("DeactivateWeatherForecastDb");

apiRoute
    .MapPost(
        "/inputweatherforecast",
        async ([FromBody] CreateWeatherForecast command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command);
            var createdEvent = result.Events.FirstOrDefault(m => m.Payload is WeatherForecastCreated)?.Payload.As<WeatherForecastCreated>();
            return Results.Ok(
                new
                {
                    success = true,
                    eventId = result.EventId,
                    aggregateId = createdEvent?.ForecastId ?? command.ForecastId,
                    sortableUniqueId = result.SortableUniqueId
                });
        })
    .WithName("InputWeatherForecast")
    .WithOpenApi();


apiRoute
    .MapPost(
        "/updateweatherforecastlocation",
        async ([FromBody] ChangeLocationName command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command);
            return Results.Ok(
                new
                {
                    success = true,
                    eventId = result.EventId,
                    aggregateId = command.ForecastId,
                    sortableUniqueId = result.SortableUniqueId
                });
        })
    .WithName("UpdateWeatherForecastLocation")
    .WithOpenApi();


// Weather Count endpoint
apiRoute
    .MapGet(
        "/weatherforecast/count",
        async (
            [FromQuery] string? waitForSortableUniqueId,
            [FromServices] ISekibanExecutor executor) =>
        {
            var query = new GetWeatherForecastCountQuery
            {
                WaitForSortableUniqueId = waitForSortableUniqueId
            };
            var countResult = await executor.QueryAsync(query);
            return Results.Ok(new
            {
                safeVersion = countResult.SafeVersion,
                unsafeVersion = countResult.UnsafeVersion,
                totalCount = countResult.TotalCount
            });
        })
    .WithOpenApi()
    .WithName("GetWeatherForecastCount");

// Weather Count endpoint for Generic projector
apiRoute
    .MapGet(
        "/weatherforecastgeneric/count",
        async (
            [FromQuery] string? waitForSortableUniqueId,
            [FromServices] ISekibanExecutor executor) =>
        {
            var query = new GetWeatherForecastCountGenericQuery
            {
                WaitForSortableUniqueId = waitForSortableUniqueId
            };
            var countResult = await executor.QueryAsync(query);
            return Results.Ok(new
            {
                safeVersion = countResult.SafeVersion,
                unsafeVersion = countResult.UnsafeVersion,
                totalCount = countResult.TotalCount,
                isGeneric = true
            });
        })
    .WithOpenApi()
    .WithName("GetWeatherForecastCountGeneric");

// Weather Count endpoint for Single projector
apiRoute
    .MapGet(
        "/weatherforecastsingle/count",
        async (
            [FromQuery] string? waitForSortableUniqueId,
            [FromServices] ISekibanExecutor executor) =>
        {
            var query = new GetWeatherForecastCountSingleQuery
            {
                WaitForSortableUniqueId = waitForSortableUniqueId
            };
            var countResult = await executor.QueryAsync(query);
            return Results.Ok(new
            {
                safeVersion = countResult.SafeVersion,
                unsafeVersion = countResult.UnsafeVersion,
                totalCount = countResult.TotalCount,
                isSingle = true
            });
        })
    .WithOpenApi()
    .WithName("GetWeatherForecastCountSingle");

// Event delivery statistics endpoint
apiRoute
    .MapGet(
        "/weatherforecast/event-statistics",
        async ([FromServices] IClusterClient client) =>
        {
            var grain = client.GetGrain<IMultiProjectionGrain>("WeatherForecastProjection");
            var stats = await grain.GetEventDeliveryStatisticsAsync();
            return Results.Ok(stats);
        })
    .WithOpenApi()
    .WithName("GetEventDeliveryStatistics");

// Event delivery statistics endpoint for Generic projector
apiRoute
    .MapGet(
        "/weatherforecastgeneric/event-statistics",
        async ([FromServices] IClusterClient client) =>
        {
            var grain = client.GetGrain<IMultiProjectionGrain>("GenericTagMultiProjector_WeatherForecastProjector_WeatherForecast");
            var stats = await grain.GetEventDeliveryStatisticsAsync();
            return Results.Ok(stats);
        })
    .WithOpenApi()
    .WithName("GetEventDeliveryStatisticsGeneric");

// Event delivery statistics endpoint for Single projector
apiRoute
    .MapGet(
        "/weatherforecastsingle/event-statistics",
        async ([FromServices] IClusterClient client) =>
        {
            var grain = client.GetGrain<IMultiProjectionGrain>("WeatherForecastProjectorWithTagStateProjector");
            var stats = await grain.GetEventDeliveryStatisticsAsync();
            return Results.Ok(stats);
        })
    .WithOpenApi()
    .WithName("GetEventDeliveryStatisticsSingle");

// Projection status endpoints (do not execute projections)
apiRoute
    .MapGet(
        "/weatherforecast/status",
        async ([FromServices] IClusterClient client) =>
        {
            var grain = client.GetGrain<IMultiProjectionGrain>("WeatherForecastProjection");
            var status = await grain.GetStatusAsync();
            return Results.Ok(status);
        })
    .WithOpenApi()
    .WithName("GetWeatherForecastStatus");

apiRoute
    .MapGet(
        "/weatherforecastgeneric/status",
        async ([FromServices] IClusterClient client) =>
        {
            var grain = client.GetGrain<IMultiProjectionGrain>("GenericTagMultiProjector_WeatherForecastProjector_WeatherForecast");
            var status = await grain.GetStatusAsync();
            return Results.Ok(status);
        })
    .WithOpenApi()
    .WithName("GetWeatherForecastGenericStatus");

apiRoute
    .MapGet(
        "/weatherforecastsingle/status",
        async ([FromServices] IClusterClient client) =>
        {
            var grain = client.GetGrain<IMultiProjectionGrain>("WeatherForecastProjectorWithTagStateProjector");
            var status = await grain.GetStatusAsync();
            return Results.Ok(status);
        })
    .WithOpenApi()
    .WithName("GetWeatherForecastSingleStatus");

// Generic projection control endpoints (for persistence + restore testing)
apiRoute
    .MapPost(
        "/projections/persist",
        async ([FromQuery] string name, [FromServices] IClusterClient client) =>
        {
            var start = DateTime.UtcNow;
            Console.WriteLine($"[PersistEndpoint] Request name={name} start={start:O}");
            var grain = client.GetGrain<IMultiProjectionGrain>(name);
            var rb = await grain.PersistStateAsync();
            var end = DateTime.UtcNow;
            if (rb.IsSuccess)
            {
                Console.WriteLine($"[PersistEndpoint] Success name={name} elapsed={(end - start).TotalMilliseconds:F1}ms");
                return Results.Ok(new { success = rb.GetValue(), elapsedMs = (end - start).TotalMilliseconds });
            }
            var err = rb.GetException()?.Message;
            Console.WriteLine($"[PersistEndpoint] Failure name={name} elapsed={(end - start).TotalMilliseconds:F1}ms error={err}");
            return Results.BadRequest(new { error = err, elapsedMs = (end - start).TotalMilliseconds });
        })
    .WithOpenApi()
    .WithName("PersistProjectionState");

apiRoute
    .MapPost(
        "/projections/deactivate",
        async ([FromQuery] string name, [FromServices] IClusterClient client) =>
        {
            var grain = client.GetGrain<IMultiProjectionGrain>(name);
            await grain.RequestDeactivationAsync();
            return Results.Ok(new { success = true });
        })
    .WithOpenApi()
    .WithName("DeactivateProjection");

apiRoute
    .MapPost(
        "/projections/refresh",
        async ([FromQuery] string name, [FromServices] IClusterClient client) =>
        {
            var grain = client.GetGrain<IMultiProjectionGrain>(name);
            await grain.RefreshAsync();
            return Results.Ok(new { success = true });
        })
    .WithOpenApi()
    .WithName("RefreshProjection");

apiRoute
    .MapGet(
        "/projections/snapshot",
        async ([FromQuery] string name, [FromQuery] bool? unsafeState, [FromServices] IClusterClient client) =>
        {
            var grain = client.GetGrain<IMultiProjectionGrain>(name);
            var rb = await grain.GetSnapshotJsonAsync(canGetUnsafeState: unsafeState ?? true);
            if (!rb.IsSuccess) return Results.BadRequest(new { error = rb.GetException()?.Message });
            return Results.Text(rb.GetValue(), "application/json");
        })
    .WithOpenApi()
    .WithName("GetProjectionSnapshot");

apiRoute
    .MapPost(
        "/projections/overwrite-version",
        async ([FromQuery] string name, [FromQuery] string newVersion, [FromServices] IClusterClient client) =>
        {
            var grain = client.GetGrain<IMultiProjectionGrain>(name);
            var ok = await grain.OverwritePersistedStateVersionAsync(newVersion);
            return ok ? Results.Ok(new { success = true }) : Results.BadRequest(new { error = "No persisted state to overwrite or invalid envelope" });
        })
    .WithOpenApi()
    .WithName("OverwriteProjectionPersistedVersion");


apiRoute
    .MapPost(
        "/removeweatherforecast",
        async ([FromBody] DeleteWeatherForecast command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command);
            return Results.Ok(
                new
                {
                    success = true,
                    eventId = result.EventId,
                    aggregateId = command.ForecastId,
                    sortableUniqueId = result.SortableUniqueId
                });
        })
    .WithName("RemoveWeatherForecast")
    .WithOpenApi();

// Health check endpoint
apiRoute.MapGet("/health", () => Results.Ok("Healthy")).WithOpenApi().WithName("HealthCheck");

apiRoute
    .MapGet(
        "/cold/status",
        async ([FromServices] IColdEventStoreFeature feature, CancellationToken ct) =>
        {
            var status = await feature.GetStatusAsync(ct);
            return Results.Ok(status);
        })
    .WithOpenApi()
    .WithName("GetColdStatus");

apiRoute
    .MapGet(
        "/cold/progress",
        async ([FromServices] IColdEventProgressReader reader, [FromServices] IServiceIdProvider serviceIdProvider, CancellationToken ct) =>
        {
            var serviceId = serviceIdProvider.GetCurrentServiceId();
            var result = await reader.GetProgressAsync(serviceId, ct);
            return result.IsSuccess
                ? Results.Ok(result.GetValue())
                : Results.BadRequest(new { error = result.GetException().Message });
        })
    .WithOpenApi()
    .WithName("GetColdProgress");

apiRoute
    .MapGet(
        "/cold/catalog",
        async ([FromServices] IColdEventCatalogReader reader, [FromServices] IServiceIdProvider serviceIdProvider, CancellationToken ct) =>
        {
            var serviceId = serviceIdProvider.GetCurrentServiceId();
            var result = await reader.GetDataRangeSummaryAsync(serviceId, ct);
            return result.IsSuccess
                ? Results.Ok(result.GetValue())
                : Results.BadRequest(new { error = result.GetException().Message });
        })
    .WithOpenApi()
    .WithName("GetColdCatalog");

apiRoute
    .MapPost(
        "/cold/export",
        async ([FromServices] IColdEventExporter exporter, [FromServices] IServiceIdProvider serviceIdProvider, CancellationToken ct) =>
        {
            var serviceId = serviceIdProvider.GetCurrentServiceId();
            var result = await exporter.ExportIncrementalAsync(serviceId, ct);
            return result.IsSuccess
                ? Results.Ok(result.GetValue())
                : Results.BadRequest(new { error = result.GetException().Message });
        })
    .WithOpenApi()
    .WithName("RunColdExport");

// Backward-compatible alias used by some manual tools/scripts.
apiRoute
    .MapPost(
        "/cold/export-now",
        async ([FromServices] IColdEventExporter exporter, [FromServices] IServiceIdProvider serviceIdProvider, CancellationToken ct) =>
        {
            var serviceId = serviceIdProvider.GetCurrentServiceId();
            var result = await exporter.ExportIncrementalAsync(serviceId, ct);
            return result.IsSuccess
                ? Results.Ok(result.GetValue())
                : Results.BadRequest(new { error = result.GetException().Message });
        })
    .WithName("RunColdExportNow")
    .ExcludeFromDescription();

if (app.Environment.IsDevelopment())
{
    // Diagnostic endpoint for cold export lock troubleshooting.
    apiRoute
        .MapGet(
            "/cold/debug",
            async (
                [FromServices] IColdObjectStorage storage,
                [FromServices] IServiceIdProvider serviceIdProvider,
                CancellationToken ct) =>
            {
                var serviceId = serviceIdProvider.GetCurrentServiceId();
                var leasePath = $"control/cold-export-{serviceId}/lease.json";
                var manifestPath = ColdStoragePaths.ManifestPath(serviceId);
                var checkpointPath = ColdStoragePaths.CheckpointPath(serviceId);

                string? leaseReadError = null;
                string? manifestReadError = null;
                string? checkpointReadError = null;

                var leaseObject = await storage.GetAsync(leasePath, ct);
                if (!leaseObject.IsSuccess)
                {
                    leaseReadError = leaseObject.GetException().Message;
                }

                var manifestObject = await storage.GetAsync(manifestPath, ct);
                if (!manifestObject.IsSuccess)
                {
                    manifestReadError = manifestObject.GetException().Message;
                }

                var checkpointObject = await storage.GetAsync(checkpointPath, ct);
                if (!checkpointObject.IsSuccess)
                {
                    checkpointReadError = checkpointObject.GetException().Message;
                }

                var segmentList = await storage.ListAsync("segments/", ct);

                return Results.Ok(new
                {
                    process = new
                    {
                        pid = Environment.ProcessId
                    },
                    serviceId,
                    lease = new
                    {
                        path = leasePath,
                        exists = leaseObject.IsSuccess,
                        error = leaseReadError
                    },
                    manifest = new
                    {
                        path = manifestPath,
                        exists = manifestObject.IsSuccess,
                        error = manifestReadError
                    },
                    checkpoint = new
                    {
                        path = checkpointPath,
                        exists = checkpointObject.IsSuccess,
                        error = checkpointReadError
                    },
                    segmentPathCount = segmentList.IsSuccess ? segmentList.GetValue().Count : -1
                });
            })
        .WithName("ColdDebug")
        .ExcludeFromDescription();
}

// Orleans test endpoint
apiRoute
    .MapGet(
        "/orleans/test",
        async ([FromServices] ISekibanExecutor executor, [FromServices] ILogger<Program> logger) =>
        {
            logger.LogInformation("Testing Orleans connectivity...");

            var query = new GetWeatherForecastListQuery();
            var result = await executor.QueryAsync(query);

            return Results.Ok(
                new
                {
                    status = "Orleans is working",
                    message = "Successfully executed query through Orleans",
                    itemCount = result.TotalCount
                });
        })
    .WithOpenApi()
    .WithName("TestOrleans");

app.MapDefaultEndpoints();

app.Run();

static async Task<bool> WaitForMaterializedViewAsync(
    IMaterializedViewGrain grain,
    string sortableUniqueId,
    TimeSpan timeout)
{
    var until = DateTime.UtcNow.Add(timeout);
    while (DateTime.UtcNow < until)
    {
        if (await grain.IsSortableUniqueIdReceived(sortableUniqueId))
        {
            return true;
        }

        await Task.Delay(100);
    }

    return false;
}

static async Task<List<WeatherForecastDbRow>> QueryWeatherForecastDbRowsAsync(
    NpgsqlConnection connection,
    MvOrleansQueryContext context,
    bool includeDeleted,
    int? pageNumber,
    int? pageSize)
{
    var forecastEntry = context.GetRequiredTable(WeatherForecastDbProjection.LogicalTable);
    var whereClause = includeDeleted ? string.Empty : "WHERE is_deleted = FALSE";
    var pagingClause = string.Empty;
    var parameters = new DynamicParameters();

    if (pageSize is > 0)
    {
        var normalizedPageSize = pageSize.Value;
        var normalizedPageNumber = Math.Max(1, pageNumber ?? 1);
        parameters.Add("Limit", normalizedPageSize);
        parameters.Add("Offset", (normalizedPageNumber - 1) * normalizedPageSize);
        pagingClause = " LIMIT @Limit OFFSET @Offset";
    }

    return (await connection.QueryAsync<WeatherForecastDbRow>(
        $"""
         SELECT forecast_id AS ForecastId,
                location AS Location,
                forecast_date::timestamp AS ForecastDate,
                temperature_c AS TemperatureC,
                summary AS Summary,
                is_deleted AS IsDeleted,
                _last_sortable_unique_id AS LastSortableUniqueId,
                _last_applied_at AS LastAppliedAt
         FROM {forecastEntry.PhysicalTable}
         {whereClause}
         ORDER BY forecast_date DESC, forecast_id
         {pagingClause};
         """,
        parameters)).ToList();
}
