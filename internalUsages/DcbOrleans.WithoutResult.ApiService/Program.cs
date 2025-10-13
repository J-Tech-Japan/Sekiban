using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.ClassRoom;
using Dcb.Domain.WithoutResult.Enrollment;
using Dcb.Domain.WithoutResult.Queries;
using Dcb.Domain.WithoutResult.Student;
using Dcb.Domain.WithoutResult.Weather;
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
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.BlobStorage.AzureStorage;
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
        var dynamicOptions = new Sekiban.Dcb.Actors.GeneralMultiProjectionActorOptions
        {
            SafeWindowMs = 20000,
            EnableDynamicSafeWindow = !builder.Configuration.GetValue<bool>("Orleans:UseInMemoryStreams"),
            MaxExtraSafeWindowMs = 30000,
            LagEmaAlpha = 0.3,
            LagDecayPerSecond = 0.98
        };
        // Per-activation scope is appropriate; Orleans constructs grains per activation
        services.AddTransient<Sekiban.Dcb.Actors.GeneralMultiProjectionActorOptions>(_ => dynamicOptions);
    });

    // Orleans will automatically discover and use the EventSurrogate
});

var domainTypes = Dcb.Domain.WithoutResult.DomainType.GetDomainTypes();
builder.Services.AddSingleton(domainTypes);

// Configure database storage based on configuration
var databaseType = builder.Configuration.GetSection("Sekiban").GetValue<string>("Database")?.ToLower();
if (databaseType == "cosmos")
{
    // CosmosDB settings - Aspire will automatically provide CosmosClient if configured
    builder.Services.AddSekibanDcbCosmosDbWithAspire();
}
else
{
    // Postgres settings (default)
    builder.Services.AddSingleton<IEventStore, PostgresEventStore>();
    builder.Services.AddSekibanDcbPostgresWithAspire();
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
builder.Services.AddTransient<ISekibanExecutorWithoutResult, OrleansDcbExecutorWithoutResult>();
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
        "/students",
        async ([FromBody] CreateStudent command, [FromServices] ISekibanExecutorWithoutResult executor) =>
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
            [FromServices] ISekibanExecutorWithoutResult executor,
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
        async (Guid studentId, [FromServices] ISekibanExecutorWithoutResult executor) =>
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
        async ([FromBody] CreateClassRoom command, [FromServices] ISekibanExecutorWithoutResult executor) =>
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
            [FromServices] ISekibanExecutorWithoutResult executor,
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
        async (Guid classRoomId, [FromServices] ISekibanExecutorWithoutResult executor) =>
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
        async ([FromBody] EnrollStudentInClassRoom command, [FromServices] ISekibanExecutorWithoutResult executor) =>
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
        async ([FromBody] DropStudentFromClassRoom command, [FromServices] ISekibanExecutorWithoutResult executor) =>
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
            var result = await eventStore.ReadAllEventsAsync();
            var events = result.GetValue().ToList();
            Console.WriteLine($"[Debug] ReadAllEventsAsync returned {events.Count} events");
            return Results.Ok(
                new
                {
                    totalEvents = events.Count,
                    events = events.Select(e => new
                    {
                        id = e.Id,
                        type = e.EventType,
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
            [FromServices] ISekibanExecutorWithoutResult executor) =>
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
            [FromServices] ISekibanExecutorWithoutResult executor) =>
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
            [FromServices] ISekibanExecutorWithoutResult executor) =>
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
    .MapPost(
        "/inputweatherforecast",
        async ([FromBody] CreateWeatherForecast command, [FromServices] ISekibanExecutorWithoutResult executor) =>
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
        async ([FromBody] ChangeLocationName command, [FromServices] ISekibanExecutorWithoutResult executor) =>
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
            [FromServices] ISekibanExecutorWithoutResult executor) =>
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
            [FromServices] ISekibanExecutorWithoutResult executor) =>
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
            [FromServices] ISekibanExecutorWithoutResult executor) =>
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
                Console.WriteLine($"[PersistEndpoint] Success name={name} elapsed={(end-start).TotalMilliseconds:F1}ms");
                return Results.Ok(new { success = rb.GetValue(), elapsedMs = (end-start).TotalMilliseconds });
            }
            var err = rb.GetException()?.Message;
            Console.WriteLine($"[PersistEndpoint] Failure name={name} elapsed={(end-start).TotalMilliseconds:F1}ms error={err}");
            return Results.BadRequest(new { error = err, elapsedMs = (end-start).TotalMilliseconds });
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
        async ([FromBody] DeleteWeatherForecast command, [FromServices] ISekibanExecutorWithoutResult executor) =>
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

// Orleans test endpoint
apiRoute
    .MapGet(
        "/orleans/test",
        async ([FromServices] ISekibanExecutorWithoutResult executor, [FromServices] ILogger<Program> logger) =>
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
