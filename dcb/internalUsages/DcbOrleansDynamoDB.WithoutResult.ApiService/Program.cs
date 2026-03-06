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
using DcbOrleansDynamoDB.WithoutResult.ApiService.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Orleans.Configuration;
using Microsoft.Extensions.Logging;
using Orleans.Storage;
using Scalar.AspNetCore;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.BlobStorage.AzureStorage;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

var builder = WebApplication.CreateBuilder(args);
const string BlobGrainDefaultType = "blob";
const string CosmosType = "cosmos";
const string PostgresType = "postgres";
const string SqliteType = "sqlite";
const string OrleansCosmosConnectionName = "OrleansCosmos";
const string OrleansClusteringTableName = "DcbOrleansClusteringTable";
const string OrleansGrainTableName = "DcbOrleansGrainTable";
const string OrleansGrainStateName = "DcbOrleansGrainState";
const string MultiProjectionOffloadName = "MultiProjectionOffload";
const string OrleansQueueName = "DcbOrleansQueue";

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

builder.AddKeyedAzureTableClient(OrleansClusteringTableName);
builder.AddKeyedAzureTableClient(OrleansGrainTableName);
builder.AddKeyedAzureBlobClient(OrleansGrainStateName);
builder.AddKeyedAzureBlobClient(MultiProjectionOffloadName);
builder.AddKeyedAzureQueueClient(OrleansQueueName);

var clusteringType = NormalizeConfigValue(builder.Configuration["ORLEANS_CLUSTERING_TYPE"]);
if (clusteringType == CosmosType ||
    NormalizeConfigValue(builder.Configuration["ORLEANS_GRAIN_DEFAULT_TYPE"]) == CosmosType)
{
    builder.AddAzureCosmosClient(OrleansCosmosConnectionName);
}

var cfgGrainDefault = NormalizeConfigValue(builder.Configuration["ORLEANS_GRAIN_DEFAULT_TYPE"]);
if (string.IsNullOrWhiteSpace(cfgGrainDefault))
{
    cfgGrainDefault = BlobGrainDefaultType;
}

var databaseType = builder.Configuration.GetSection("Sekiban").GetValue<string>("Database")?.ToLower() ?? SqliteType;
var coldEventEnabled = ResolveColdEventEnabled(builder.Configuration);

// Configure Orleans
builder.UseOrleans(config =>
{
    if (builder.Environment.IsDevelopment())
    {
        config.UseLocalhostClustering();
    }
    else
    {
        if (clusteringType == CosmosType)
        {
            var connectionString = builder.Configuration.GetConnectionString(OrleansCosmosConnectionName) ??
                                   throw new InvalidOperationException();
            config.UseCosmosClustering(options =>
            {
                options.ConfigureCosmosClient(connectionString);
            });
        }
        else
        {
            config.UseAzureStorageClustering(options =>
            {
                options.Configure<IServiceProvider>((clusteringOptions, sp) =>
                {
                    clusteringOptions.TableServiceClient = sp.GetRequiredKeyedService<TableServiceClient>(OrleansClusteringTableName);
                });
            });
        }
    }

    var useInMemoryStreams = builder.Configuration.GetValue<bool>("Orleans:UseInMemoryStreams");

    if (useInMemoryStreams)
    {
        config.AddMemoryStreams("EventStreamProvider", configurator =>
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

        config.AddMemoryStreams(OrleansQueueName, configurator =>
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

        config.AddMemoryGrainStorage("PubSubStore");
    }
    else
    {
        config.AddAzureQueueStreams(
            "EventStreamProvider",
            configurator =>
            {
                configurator.ConfigureAzureQueue(options =>
                {
                    options.Configure<IServiceProvider>((queueOptions, sp) =>
                    {
                        queueOptions.QueueServiceClient = sp.GetKeyedService<QueueServiceClient>(OrleansQueueName);
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
            OrleansQueueName,
            configurator =>
            {
                configurator.ConfigureAzureQueue(options =>
                {
                    options.Configure<IServiceProvider>((queueOptions, sp) =>
                    {
                        queueOptions.QueueServiceClient = sp.GetKeyedService<QueueServiceClient>(OrleansQueueName);
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

    Console.WriteLine($"UseOrleans: ORLEANS_GRAIN_DEFAULT_TYPE={cfgGrainDefault}");
    if (cfgGrainDefault == CosmosType)
    {
        config.AddCosmosGrainStorageAsDefault(options =>
        {
            var connectionString = builder.Configuration.GetConnectionString(OrleansCosmosConnectionName) ??
                                   throw new InvalidOperationException();
            options.ConfigureCosmosClient(connectionString);
            options.IsResourceCreationEnabled = true;
        });
    }
    else
    {
        config.AddAzureBlobGrainStorageAsDefault(options =>
        {
            options.Configure<IServiceProvider>((opt, sp) =>
            {
                opt.BlobServiceClient = sp.GetKeyedService<BlobServiceClient>(OrleansGrainStateName);
                opt.ContainerName = "sekiban-grainstate";
            });
        });
    }

    if (cfgGrainDefault == CosmosType)
    {
        config.AddCosmosGrainStorage(
            "OrleansStorage",
            options =>
            {
                var connectionString = builder.Configuration.GetConnectionString(OrleansCosmosConnectionName) ??
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
                    opt.BlobServiceClient = sp.GetKeyedService<BlobServiceClient>(OrleansGrainStateName);
                    opt.ContainerName = "sekiban-grainstate";
                });
            });
    }

    if (cfgGrainDefault == CosmosType)
    {
        config.AddCosmosGrainStorage(
            "dcb-orleans-queue",
            options =>
            {
                var connectionString = builder.Configuration.GetConnectionString(OrleansCosmosConnectionName) ??
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
                    opt.BlobServiceClient = sp.GetKeyedService<BlobServiceClient>(OrleansGrainStateName);
                    opt.ContainerName = "sekiban-grainstate";
                });
            });
    }

    if (cfgGrainDefault == CosmosType)
    {
        config.AddCosmosGrainStorage(
            OrleansGrainTableName,
            options =>
            {
                var connectionString = builder.Configuration.GetConnectionString(OrleansCosmosConnectionName) ??
                                       throw new InvalidOperationException();
                options.ConfigureCosmosClient(connectionString);
                options.IsResourceCreationEnabled = true;
            });
    }
    else
    {
        config.AddAzureTableGrainStorage(
            OrleansGrainTableName,
            options =>
            {
                options.Configure<IServiceProvider>((opt, sp) =>
                {
                    opt.TableServiceClient = sp.GetKeyedService<TableServiceClient>(OrleansGrainTableName);
                });
            });
    }

    if (!useInMemoryStreams)
    {
        if (cfgGrainDefault == CosmosType)
        {
            config.AddCosmosGrainStorage(
                "PubSubStore",
                options =>
                {
                    var connectionString = builder.Configuration.GetConnectionString(OrleansCosmosConnectionName) ??
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
                        opt.TableServiceClient = sp.GetKeyedService<TableServiceClient>(OrleansGrainTableName);
                        opt.GrainStorageSerializer = sp.GetRequiredService<NewtonsoftJsonDcbOrleansSerializer>();
                    });
                    options.Configure<IGrainStorageSerializer>((op, serializer) => op.GrainStorageSerializer = serializer);
                });
        }
    }

    if (cfgGrainDefault == CosmosType)
    {
        config.AddCosmosGrainStorage(
            "EventStreamProvider",
            options =>
            {
                var connectionString = builder.Configuration.GetConnectionString(OrleansCosmosConnectionName) ??
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
                    opt.TableServiceClient = sp.GetKeyedService<TableServiceClient>(OrleansGrainTableName);
                    opt.GrainStorageSerializer = sp.GetRequiredService<NewtonsoftJsonDcbOrleansSerializer>();
                });
                options.Configure<IGrainStorageSerializer>((op, serializer) => op.GrainStorageSerializer = serializer);
            });
    }

    config.ConfigureServices(services =>
    {
        services.AddTransient<IGrainStorageSerializer, NewtonsoftJsonDcbOrleansSerializer>();
        if (builder.Environment.IsDevelopment())
        {
            services.AddTransient<Sekiban.Dcb.MultiProjections.IMultiProjectionEventStatistics, Sekiban.Dcb.MultiProjections.RecordingMultiProjectionEventStatistics>();
        }
        else
        {
            services.AddTransient<Sekiban.Dcb.MultiProjections.IMultiProjectionEventStatistics, Sekiban.Dcb.MultiProjections.NoOpMultiProjectionEventStatistics>();
        }

        var dynamicOptions = new GeneralMultiProjectionActorOptions
        {
            SafeWindowMs = databaseType == SqliteType ? 5000 : 20000,
            EnableDynamicSafeWindow = databaseType != SqliteType &&
                                      !builder.Configuration.GetValue<bool>("Orleans:UseInMemoryStreams"),
            MaxExtraSafeWindowMs = 30000,
            LagEmaAlpha = 0.3,
            LagDecayPerSecond = 0.98
        };
        services.AddTransient<GeneralMultiProjectionActorOptions>(_ => dynamicOptions);
    });
});

var domainTypes = DomainType.GetDomainTypes();
builder.Services.AddSingleton(domainTypes);

builder.Services.AddSekibanDcbNativeRuntime();
builder.Services.AddSekibanDcbColdEventDefaults();

if (databaseType == CosmosType)
{
    builder.Services.AddSekibanDcbCosmosDbWithAspire();
    builder.Services.AddSingleton<IMultiProjectionStateStore, CosmosMultiProjectionStateStore>();
}
else if (databaseType == SqliteType)
{
    var sqliteCacheDir = builder.Configuration.GetValue<string>("Sekiban:SqliteCachePath") ??
                         Directory.GetCurrentDirectory();
    var sqliteCachePath = Path.Combine(sqliteCacheDir, "events.db");
    Console.WriteLine($"Using SQLite database: {sqliteCachePath}");
    builder.Services.AddSekibanDcbSqlite(sqliteCachePath);
}
else if (databaseType == PostgresType)
{
    builder.Services.AddSekibanDcbPostgresWithAspire("DcbPostgres");
    builder.Services.AddSingleton<IMultiProjectionStateStore, PostgresMultiProjectionStateStore>();
}
else
{
    throw new InvalidOperationException(
        $"Unsupported Sekiban:Database '{databaseType}'. Supported values are sqlite, cosmos, postgres.");
}

if (coldEventEnabled)
{
    builder.Services.AddSekibanDcbColdExport(
        builder.Configuration,
        builder.Environment.ContentRootPath,
        addBackgroundService: false);
    builder.Services.AddSekibanDcbColdEventHybridRead();
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
    var blobServiceClient = sp.GetRequiredKeyedService<BlobServiceClient>(MultiProjectionOffloadName);
    return new AzureBlobStorageSnapshotAccessor(blobServiceClient, "multiprojection-snapshot-offload");
});
builder.Services.AddTransient<ISekibanExecutor, OrleansDcbExecutor>();
builder.Services.AddScoped<IActorObjectAccessor, OrleansActorObjectAccessor>();

if (builder.Environment.IsDevelopment())
{
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

var apiRoute = app.MapGroup("/api");

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    app.UseCors();
}

// Student endpoints
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

if (app.Environment.IsDevelopment() && coldEventEnabled)
{
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

static string NormalizeConfigValue(string? value) => value?.ToLowerInvariant() ?? string.Empty;

static bool ResolveColdEventEnabled(IConfiguration configuration)
{
    var coldConfig = configuration.GetSection("Sekiban:ColdEvent");
    var configuredOptions = coldConfig.Get<ColdEventStoreOptions>() ?? new ColdEventStoreOptions();
    return string.IsNullOrWhiteSpace(coldConfig["Enabled"]) || configuredOptions.Enabled;
}
