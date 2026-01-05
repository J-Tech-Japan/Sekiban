using System.Net;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Core.Extensions;
using Microsoft.Extensions.Azure;
using Dcb.Domain;
using Dcb.Domain.ClassRoom;
using Dcb.Domain.Enrollment;
using Dcb.Domain.Queries;
using Dcb.Domain.Student;
using Dcb.Domain.Weather;
using Microsoft.AspNetCore.Mvc;
using Orleans.Configuration;
using Orleans.Storage;
using Scalar.AspNetCore;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.BlobStorage.AzureStorage;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddFilter("Azure.Core", LogLevel.Error);
    builder.Logging.AddFilter("Azure.Storage", LogLevel.Error);
    builder.Logging.AddFilter("Orleans.AzureUtils", LogLevel.Error);
}

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Log key configuration switches early for troubleshooting
var cfgClustering = (builder.Configuration["ORLEANS_CLUSTERING_TYPE"] ?? "").ToLower();
var cfgGrainDefault = (builder.Configuration["ORLEANS_GRAIN_DEFAULT_TYPE"] ?? "").ToLower();
var cfgQueueType = (builder.Configuration["ORLEANS_QUEUE_TYPE"] ?? "").ToLower();
Console.WriteLine($"Config switches => ORLEANS_CLUSTERING_TYPE={cfgClustering}, ORLEANS_GRAIN_DEFAULT_TYPE={cfgGrainDefault}, ORLEANS_QUEUE_TYPE={cfgQueueType}");
if ((builder.Configuration["ORLEANS_CLUSTERING_TYPE"] ?? "").ToLower() != "cosmos")
    Console.WriteLine("Registering keyed Azure Table ServiceClient: DcbOrleansClusteringTable");
    builder.AddKeyedAzureTableServiceClient("DcbOrleansClusteringTable");

if ((builder.Configuration["ORLEANS_GRAIN_DEFAULT_TYPE"] ?? "").ToLower() != "cosmos")
    Console.WriteLine("Registering keyed Azure Blob ServiceClient: DcbOrleansGrainState");
    builder.AddKeyedAzureBlobServiceClient("DcbOrleansGrainState");

if ((builder.Configuration["ORLEANS_GRAIN_DEFAULT_TYPE"] ?? "").ToLower() != "cosmos" ||
    (builder.Configuration["ORLEANS_QUEUE_TYPE"] ?? "").ToLower() == "eventhub")
    Console.WriteLine("Registering keyed Azure Table ServiceClient: DcbOrleansGrainTable");
    builder.AddKeyedAzureTableServiceClient("DcbOrleansGrainTable");

Console.WriteLine("Registering keyed Azure Blob ServiceClient: MultiProjectionOffload");
builder.AddKeyedAzureBlobServiceClient("MultiProjectionOffload");
Console.WriteLine("Registering keyed Azure Queue ServiceClient: DcbOrleansQueue");
builder.AddKeyedAzureQueueServiceClient("DcbOrleansQueue");

// Ensure Orleans fallbacks (GetRequiredKeyedService) can resolve clients even if options aren't set
builder.Services.AddKeyedSingleton<QueueServiceClient>(
    "DcbOrleansQueue",
    (sp, _) => sp.GetRequiredService<IAzureClientFactory<QueueServiceClient>>().CreateClient("DcbOrleansQueue"));
// Some Orleans Azure Queue configurations use the provider name as the DI key.
// Register an alias for the EventStreamProvider as well, resolving via the same connection.
builder.Services.AddKeyedSingleton<QueueServiceClient>(
    "EventStreamProvider",
    (sp, _) => sp.GetRequiredService<IAzureClientFactory<QueueServiceClient>>().CreateClient("DcbOrleansQueue"));
builder.Services.AddKeyedSingleton<BlobServiceClient>(
    "DcbOrleansGrainState",
    (sp, _) => sp.GetRequiredService<IAzureClientFactory<BlobServiceClient>>().CreateClient("DcbOrleansGrainState"));
builder.Services.AddKeyedSingleton<BlobServiceClient>(
    "MultiProjectionOffload",
    (sp, _) => sp.GetRequiredService<IAzureClientFactory<BlobServiceClient>>().CreateClient("MultiProjectionOffload"));

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

    var useInMemoryStreams = builder.Configuration.GetValue<bool>("Orleans:UseInMemoryStreams");
    if (useInMemoryStreams)
    {
        config.AddMemoryStreams("EventStreamProvider", configurator =>
        {
            configurator.ConfigurePartitioning();
            configurator.ConfigurePullingAgent(options =>
            {
                options.Configure(opt =>
                {
                    opt.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(100);
                    opt.BatchContainerBatchSize = 100;
                });
            });
        });
        config.AddMemoryStreams("DcbOrleansQueue", configurator =>
        {
            configurator.ConfigurePartitioning();
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
        if ((builder.Configuration["ORLEANS_QUEUE_TYPE"] ?? "").ToLower() == "eventhub")
        {
            config.AddEventHubStreams(
                "EventStreamProvider",
                configurator =>
                {
                    // Existing Event Hub connection settings
                    configurator.ConfigureEventHub(ob => ob.Configure(options =>
                    {
                        options.ConfigureEventHubConnection(
                            builder.Configuration.GetConnectionString("OrleansEventHub"),
                            builder.Configuration["ORLEANS_QUEUE_EVENTHUB_NAME"],
                            "$Default");
                    }));
                    // 🔑 NEW –‑ tell Orleans where to persist checkpoints
                    configurator.UseAzureTableCheckpointer(ob => ob.Configure(cp =>
                    {
                        cp.TableName = "EventHubCheckpointsEventStreamsProvider"; // any table name you like
                        cp.PersistInterval = TimeSpan.FromSeconds(10); // write frequency
                        cp.TableServiceClient = new TableServiceClient(
                            builder.Configuration.GetConnectionString("DcbOrleansGrainTable"));
                    }));
                });
            config.AddEventHubStreams(
                "DcbOrleansQueue",
                configurator =>
                {
                    // Existing Event Hub connection settings
                    configurator.ConfigureEventHub(ob => ob.Configure(options =>
                    {
                        options.ConfigureEventHubConnection(
                            builder.Configuration.GetConnectionString("OrleansEventHub"),
                            builder.Configuration["ORLEANS_QUEUE_EVENTHUB_NAME"],
                            "$Default");
                    }));

                    // 🔑 NEW –‑ tell Orleans where to persist checkpoints
                    configurator.UseAzureTableCheckpointer(ob => ob.Configure(cp =>
                    {
                        cp.TableName = "EventHubCheckpointsOrleansSekibanQueue"; // any table name you like
                        cp.PersistInterval = TimeSpan.FromSeconds(10); // write frequency
                        cp.TableServiceClient = new TableServiceClient(
                            builder.Configuration.GetConnectionString("DcbOrleansGrainTable"));
                    }));

                    // …your cache, queue‑mapper, pulling‑agent settings remain unchanged …
                });
        }
        else
        {
            config.AddAzureQueueStreams(
                "EventStreamProvider",
                options =>
                {
                    options.Configure<IServiceProvider>((queueOptions, sp) =>
                    {
                        queueOptions.QueueServiceClient = new QueueServiceClient(
                            builder.Configuration.GetConnectionString("DcbOrleansQueue"));
                        queueOptions.QueueNames =
                        [
                            "dcborleans-eventstreamprovider-0",
                            "dcborleans-eventstreamprovider-1",
                            "dcborleans-eventstreamprovider-2"
                        ];
                        queueOptions.MessageVisibilityTimeout = TimeSpan.FromMinutes(2);
                    });
                });
            config.ConfigureServices(services =>
            {
                services.Configure<HashRingStreamQueueMapperOptions>(
                    "EventStreamProvider",
                    o => o.TotalQueueCount = 3);
                services.Configure<StreamPullingAgentOptions>(
                    "EventStreamProvider",
                    opt =>
                    {
                        opt.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(1000);
                        opt.BatchContainerBatchSize = 256;
                        opt.StreamInactivityPeriod = TimeSpan.FromMinutes(10);
                    });
            });
            config.AddAzureQueueStreams(
                "DcbOrleansQueue",
                options =>
                {
                    options.Configure<IServiceProvider>((queueOptions, sp) =>
                    {
                        queueOptions.QueueServiceClient = new QueueServiceClient(
                            builder.Configuration.GetConnectionString("DcbOrleansQueue"));
                        queueOptions.QueueNames =
                        [
                            "dcborleans-queue-0",
                            "dcborleans-queue-1",
                            "dcborleans-queue-2"
                        ];
                        queueOptions.MessageVisibilityTimeout = TimeSpan.FromMinutes(2);
                    });
                });
            config.ConfigureServices(services =>
            {
                services.Configure<HashRingStreamQueueMapperOptions>(
                    "DcbOrleansQueue",
                    o => o.TotalQueueCount = 3);
                services.Configure<StreamPullingAgentOptions>(
                    "DcbOrleansQueue",
                    opt =>
                    {
                        opt.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(1000);
                        opt.BatchContainerBatchSize = 256;
                        opt.StreamInactivityPeriod = TimeSpan.FromMinutes(10);
                    });
            });
        }
    }

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
        config.AddCosmosGrainStorage(
            "OrleansStorage",
            options =>
            {
                var connectionString = builder.Configuration.GetConnectionString("OrleansCosmos") ??
                                       throw new InvalidOperationException();
                options.ConfigureCosmosClient(connectionString);
                options.IsResourceCreationEnabled = true;
            });
        config.AddCosmosGrainStorage(
            "dcb-orleans-queue",
            options =>
            {
                var connectionString = builder.Configuration.GetConnectionString("OrleansCosmos") ??
                                       throw new InvalidOperationException();
                options.ConfigureCosmosClient(connectionString);
                options.IsResourceCreationEnabled = true;
            });
        config.AddCosmosGrainStorage(
            "DcbOrleansGrainTable",
            options =>
            {
                var connectionString = builder.Configuration.GetConnectionString("OrleansCosmos") ??
                                       throw new InvalidOperationException();
                options.ConfigureCosmosClient(connectionString);
                options.IsResourceCreationEnabled = true;
            });
        config.AddCosmosGrainStorage(
            "EventStreamProvider",
            options =>
            {
                var connectionString = builder.Configuration.GetConnectionString("OrleansCosmos") ??
                                       throw new InvalidOperationException();
                options.ConfigureCosmosClient(connectionString);
                options.IsResourceCreationEnabled = true;
            });
        if (!useInMemoryStreams)
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
        Console.WriteLine("Configuring Azure Blob as default grain storage (non-cosmos setting)");
        config.AddAzureBlobGrainStorageAsDefault(options =>
        {
            options.Configure<IServiceProvider>((opt, sp) =>
            {
                var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("StorageConfig");
                var client = sp.GetKeyedService<BlobServiceClient>("DcbOrleansGrainState");
                if (client is null)
                {
                    var hasConn = !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DcbOrleansGrainState"));
                    logger?.LogError("BlobServiceClient(DcbOrleansGrainState) not resolved. ConnectionStrings:DcbOrleansGrainState present={present}", hasConn);
                    Console.WriteLine($"ERROR: BlobServiceClient(DcbOrleansGrainState) not resolved. ConnectionStrings:DcbOrleansGrainState present={hasConn}");
                }
                opt.BlobServiceClient = client;
                opt.ContainerName = "sekiban-grainstate";
            });
        });
        config.AddAzureBlobGrainStorage(
            "OrleansStorage",
            options =>
            {
                options.Configure<IServiceProvider>((opt, sp) =>
                {
                    var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("StorageConfig");
                    var client = sp.GetKeyedService<BlobServiceClient>("DcbOrleansGrainState");
                    if (client is null) logger?.LogError("BlobServiceClient(DcbOrleansGrainState) not resolved for OrleansStorage");
                    opt.BlobServiceClient = client;
                    opt.ContainerName = "sekiban-grainstate";
                });
            });
        config.AddAzureBlobGrainStorage(
            "dcb-orleans-queue",
            options =>
            {
                options.Configure<IServiceProvider>((opt, sp) =>
                {
                    var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("StorageConfig");
                    var client = sp.GetKeyedService<BlobServiceClient>("DcbOrleansGrainState");
                    if (client is null) logger?.LogError("BlobServiceClient(DcbOrleansGrainState) not resolved for dcb-orleans-queue");
                    opt.BlobServiceClient = client;
                    opt.ContainerName = "sekiban-grainstate";
                });
            });
        config.AddAzureTableGrainStorage(
            "DcbOrleansGrainTable",
            options =>
            {
                options.Configure<IServiceProvider>((opt, sp) =>
                {
                    opt.TableServiceClient = sp.GetKeyedService<TableServiceClient>("DcbOrleansGrainTable");
                });
            });
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
        if (!useInMemoryStreams)
            config.AddAzureTableGrainStorage(
                "PubSubStore",
                options =>
                {
                    options.Configure<IServiceProvider>((opt, sp) =>
                    {
                        opt.TableServiceClient = sp.GetKeyedService<TableServiceClient>("DcbOrleansGrainTable");
                        opt.GrainStorageSerializer = sp.GetRequiredService<NewtonsoftJsonDcbOrleansSerializer>();
                    });
                    options.Configure<IGrainStorageSerializer>((op, serializer) =>
                        op.GrainStorageSerializer = serializer);
                });
    }


    // Check for VNet IP Address from environment variable APP Service specific setting
    if (!string.IsNullOrWhiteSpace(builder.Configuration["WEBSITE_PRIVATE_IP"]) &&
        !string.IsNullOrWhiteSpace(builder.Configuration["WEBSITE_PRIVATE_PORTS"]))
    {
        // Get IP and ports from environment variables
        var ip = IPAddress.Parse(builder.Configuration["WEBSITE_PRIVATE_IP"]!);
        var ports = builder.Configuration["WEBSITE_PRIVATE_PORTS"]!.Split(',');
        if (ports.Length < 2) throw new Exception("Insufficient number of private ports");
        int siloPort = int.Parse(ports[0]), gatewayPort = int.Parse(ports[1]);
        Console.WriteLine($"Using WEBSITE_PRIVATE_IP: {ip}, siloPort: {siloPort}, gatewayPort: {gatewayPort}");
        config.ConfigureEndpoints(ip, siloPort, gatewayPort, true);
    }

    config.ConfigureServices(services =>
    {
        services.AddTransient<IGrainStorageSerializer, NewtonsoftJsonDcbOrleansSerializer>();
        if (builder.Environment.IsDevelopment())
            services.AddTransient<IMultiProjectionEventStatistics, RecordingMultiProjectionEventStatistics>();
        else
            services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
        var dynamicOptions = new GeneralMultiProjectionActorOptions
        {
            SafeWindowMs = 20000,
            EnableDynamicSafeWindow = !builder.Configuration.GetValue<bool>("Orleans:UseInMemoryStreams"),
            MaxExtraSafeWindowMs = 30000,
            LagEmaAlpha = 0.3,
            LagDecayPerSecond = 0.98
        };
        services.AddTransient<GeneralMultiProjectionActorOptions>(_ => dynamicOptions);
    });
});
var domainTypes = DomainType.GetDomainTypes();
builder.Services.AddSingleton(domainTypes);
var databaseType = builder.Configuration.GetSection("Sekiban").GetValue<string>("Database")?.ToLower();
if (databaseType == "cosmos")
{
    builder.Services.AddSekibanDcbCosmosDbWithAspire();
    builder.Services.AddSingleton<IMultiProjectionStateStore, Sekiban.Dcb.CosmosDb.CosmosMultiProjectionStateStore>();
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
// Snapshot offload: Azure Blob Storage accessor using Aspire-configured BlobServiceClient
// Using a minimal implementation since Sekiban.Dcb.BlobStorage.AzureStorage is not yet published
builder.Services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
{
    var blobServiceClient = sp.GetRequiredKeyedService<BlobServiceClient>("MultiProjectionOffload");
    return new AzureBlobStorageSnapshotAccessor(blobServiceClient, "multiprojection-snapshot-offload");
});
builder.Services.AddTransient<ISekibanExecutor, OrleansDcbExecutor>();
builder.Services.AddScoped<IActorObjectAccessor, OrleansActorObjectAccessor>();
if (builder.Environment.IsDevelopment())
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAll", policy => { policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod(); });
    });
var app = builder.Build();
var apiRoute = app.MapGroup("/api");
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    app.UseCors("AllowAll");
}

apiRoute
    .MapPost(
        "/students",
        async ([FromBody] CreateStudent command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command);
            if (result.IsSuccess)
                return Results.Ok(
                    new
                    {
                        studentId = command.StudentId,
                        eventId = result.GetValue().EventId,
                        sortableUniqueId = result.GetValue().SortableUniqueId,
                        message = "Student created successfully"
                    });
            return Results.BadRequest(new { error = result.GetException().Message });
        })
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
            if (result.IsSuccess)
            {
                var queryResult = result.GetValue();
                return Results.Ok(queryResult.Items);
            }

            return Results.BadRequest(new { error = result.GetException().Message });
        })
        .WithName("GetStudentList");
apiRoute
    .MapGet(
        "/students/{studentId:guid}",
        async (Guid studentId, [FromServices] ISekibanExecutor executor) =>
        {
            var tag = new StudentTag(studentId);
            var tagStateId = new TagStateId(tag, nameof(StudentProjector));
            var result = await executor.GetTagStateAsync(tagStateId);
            if (result.IsSuccess)
            {
                var state = result.GetValue();
                return Results.Ok(
                    new
                    {
                        studentId,
                        payload = state.Payload as dynamic,
                        version = state.Version
                    });
            }

            return Results.NotFound(new { error = $"Student {studentId} not found" });
        })
        .WithName("GetStudent");
apiRoute
    .MapPost(
        "/classrooms",
        async ([FromBody] CreateClassRoom command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command, CreateClassRoomHandler.HandleAsync);
            if (result.IsSuccess)
                return Results.Ok(
                    new
                    {
                        classRoomId = command.ClassRoomId,
                        eventId = result.GetValue().EventId,
                        sortableUniqueId = result.GetValue().SortableUniqueId,
                        message = "ClassRoom created successfully"
                    });
            return Results.BadRequest(new { error = result.GetException().Message });
        })
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
            if (result.IsSuccess)
            {
                var queryResult = result.GetValue();
                return Results.Ok(queryResult.Items);
            }

            return Results.BadRequest(new { error = result.GetException().Message });
        })
        .WithName("GetClassRoomList");
apiRoute
    .MapGet(
        "/classrooms/{classRoomId:guid}",
        async (Guid classRoomId, [FromServices] ISekibanExecutor executor) =>
        {
            var tag = new ClassRoomTag(classRoomId);
            var tagStateId = new TagStateId(tag, nameof(ClassRoomProjector));
            var result = await executor.GetTagStateAsync(tagStateId);
            if (result.IsSuccess)
            {
                var state = result.GetValue();
                return Results.Ok(
                    new
                    {
                        classRoomId,
                        payload = state.Payload,
                        version = state.Version
                    });
            }

            return Results.NotFound(new { error = $"ClassRoom {classRoomId} not found" });
        })
        .WithName("GetClassRoom");
apiRoute
    .MapPost(
        "/enrollments/add",
        async ([FromBody] EnrollStudentInClassRoom command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command, EnrollStudentInClassRoomHandler.HandleAsync);
            if (result.IsSuccess)
                return Results.Ok(
                    new
                    {
                        studentId = command.StudentId,
                        classRoomId = command.ClassRoomId,
                        eventId = result.GetValue().EventId,
                        sortableUniqueId = result.GetValue().SortableUniqueId,
                        message = "Student enrolled successfully"
                    });
            return Results.BadRequest(new { error = result.GetException().Message });
        })
        .WithName("EnrollStudent");
apiRoute
    .MapPost(
        "/enrollments/drop",
        async ([FromBody] DropStudentFromClassRoom command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command, DropStudentFromClassRoomHandler.HandleAsync);
            if (result.IsSuccess)
                return Results.Ok(
                    new
                    {
                        studentId = command.StudentId,
                        classRoomId = command.ClassRoomId,
                        eventId = result.GetValue().EventId,
                        sortableUniqueId = result.GetValue().SortableUniqueId,
                        message = "Student dropped successfully"
                    });
            return Results.BadRequest(new { error = result.GetException().Message });
        })
        .WithName("DropStudent");
apiRoute
    .MapGet(
        "/debug/events",
        async ([FromServices] IEventStore eventStore) =>
        {
            try
            {
                var result = await eventStore.ReadAllEventsAsync();
                if (result.IsSuccess)
                {
                    var events = result.GetValue().ToList();
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
                }

                return Results.BadRequest(new { error = result.GetException()?.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        })
        .WithName("DebugGetEvents");
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
            if (result.IsSuccess)
            {
                var queryResult = result.GetValue();
                return Results.Ok(queryResult.Items);
            }

            return Results.BadRequest(new { error = result.GetException()?.Message });
        })
        .WithName("GetWeatherForecast");
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
            if (result.IsSuccess)
            {
                var queryResult = result.GetValue();
                return Results.Ok(queryResult.Items);
            }

            return Results.BadRequest(new { error = result.GetException()?.Message });
        })
        .WithName("GetWeatherForecastGeneric");
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
            if (result.IsSuccess)
            {
                var queryResult = result.GetValue();
                return Results.Ok(queryResult.Items);
            }

            return Results.BadRequest(new { error = result.GetException()?.Message });
        })
        .WithName("GetWeatherForecastSingle");
apiRoute
    .MapPost(
        "/inputweatherforecast",
        async ([FromBody] CreateWeatherForecast command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command);
            if (result.IsSuccess)
                return Results.Ok(
                    new
                    {
                        success = true,
                        eventId = result.GetValue().EventId,
                        aggregateId = result.GetValue().Events.FirstOrDefault(m => m.Payload is WeatherForecastCreated)?.Payload.As<WeatherForecastCreated>()?.ForecastId,
                        sortableUniqueId = result.GetValue().SortableUniqueId
                    });
            return Results.BadRequest(
                new
                {
                    success = false,
                    error = result.GetException()?.Message
                });
        })
    .WithName("InputWeatherForecast");

apiRoute
    .MapPost(
        "/updateweatherforecastlocation",
        async ([FromBody] ChangeLocationName command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command);
            if (result.IsSuccess)
                return Results.Ok(
                    new
                    {
                        success = true,
                        eventId = result.GetValue().EventId,
                        aggregateId = command.ForecastId,
                        sortableUniqueId = result.GetValue().SortableUniqueId
                    });
            return Results.BadRequest(
                new
                {
                    success = false,
                    error = result.GetException()?.Message
                });
        })
    .WithName("UpdateWeatherForecastLocation");

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
            var result = await executor.QueryAsync(query);
            if (result.IsSuccess)
            {
                var countResult = result.GetValue();
                return Results.Ok(new
                {
                    safeVersion = countResult.SafeVersion,
                    unsafeVersion = countResult.UnsafeVersion,
                    totalCount = countResult.TotalCount
                });
            }

            return Results.BadRequest(new { error = result.GetException()?.Message ?? "Query failed" });
        })
        .WithName("GetWeatherForecastCount");
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
            var result = await executor.QueryAsync(query);
            if (result.IsSuccess)
            {
                var countResult = result.GetValue();
                return Results.Ok(new
                {
                    safeVersion = countResult.SafeVersion,
                    unsafeVersion = countResult.UnsafeVersion,
                    totalCount = countResult.TotalCount,
                    isGeneric = true
                });
            }

            return Results.BadRequest(new { error = result.GetException()?.Message ?? "Query failed" });
        })
        .WithName("GetWeatherForecastCountGeneric");
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
            var result = await executor.QueryAsync(query);
            if (result.IsSuccess)
            {
                var countResult = result.GetValue();
                return Results.Ok(new
                {
                    safeVersion = countResult.SafeVersion,
                    unsafeVersion = countResult.UnsafeVersion,
                    totalCount = countResult.TotalCount,
                    isSingle = true
                });
            }

            return Results.BadRequest(new { error = result.GetException()?.Message ?? "Query failed" });
        })
        .WithName("GetWeatherForecastCountSingle");
apiRoute
    .MapGet(
        "/weatherforecast/event-statistics",
        async ([FromServices] IClusterClient client) =>
        {
            var grain = client.GetGrain<IMultiProjectionGrain>("WeatherForecastProjection");
            var stats = await grain.GetEventDeliveryStatisticsAsync();
            return Results.Ok(stats);
        })
        .WithName("GetEventDeliveryStatistics");
apiRoute
    .MapGet(
        "/weatherforecastgeneric/event-statistics",
        async ([FromServices] IClusterClient client) =>
        {
            var grain = client.GetGrain<IMultiProjectionGrain>(
                "GenericTagMultiProjector_WeatherForecastProjector_WeatherForecast");
            var stats = await grain.GetEventDeliveryStatisticsAsync();
            return Results.Ok(stats);
        })
        .WithName("GetEventDeliveryStatisticsGeneric");
apiRoute
    .MapGet(
        "/weatherforecastsingle/event-statistics",
        async ([FromServices] IClusterClient client) =>
        {
            var grain = client.GetGrain<IMultiProjectionGrain>("WeatherForecastProjectorWithTagStateProjector");
            var stats = await grain.GetEventDeliveryStatisticsAsync();
            return Results.Ok(stats);
        })
        .WithName("GetEventDeliveryStatisticsSingle");
apiRoute
    .MapGet(
        "/weatherforecast/status",
        async ([FromServices] IClusterClient client) =>
        {
            try
            {
                var grain = client.GetGrain<IMultiProjectionGrain>("WeatherForecastProjection");
                var status = await grain.GetStatusAsync();
                return Results.Ok(status);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { projector = "WeatherForecastProjection", error = ex.Message });
            }
        })
        .WithName("GetWeatherForecastStatus");
apiRoute
    .MapGet(
        "/weatherforecastgeneric/status",
        async ([FromServices] IClusterClient client) =>
        {
            var grain = client.GetGrain<IMultiProjectionGrain>(
                "GenericTagMultiProjector_WeatherForecastProjector_WeatherForecast");
            var status = await grain.GetStatusAsync();
            return Results.Ok(status);
        })
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
        .WithName("GetWeatherForecastSingleStatus");
apiRoute
    .MapPost(
        "/projections/persist",
        async ([FromQuery] string name, [FromServices] IClusterClient client) =>
        {
            try
            {
                var start = DateTime.UtcNow;
                var grain = client.GetGrain<IMultiProjectionGrain>(name);
                var rb = await grain.PersistStateAsync();
                var end = DateTime.UtcNow;
                if (rb.IsSuccess)
                    return Results.Ok(new { success = rb.GetValue(), elapsedMs = (end - start).TotalMilliseconds });
                var err = rb.GetException()?.Message;
                return Results.BadRequest(new { error = err, elapsedMs = (end - start).TotalMilliseconds });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("PersistProjectionState");
apiRoute
    .MapPost(
        "/projections/deactivate",
        async ([FromQuery] string name, [FromServices] IClusterClient client) =>
        {
            try
            {
                var grain = client.GetGrain<IMultiProjectionGrain>(name);
                await grain.RequestDeactivationAsync();
                return Results.Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("DeactivateProjection");
apiRoute
    .MapPost(
        "/projections/refresh",
        async ([FromQuery] string name, [FromServices] IClusterClient client) =>
        {
            try
            {
                var grain = client.GetGrain<IMultiProjectionGrain>(name);
                await grain.RefreshAsync();
                return Results.Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("RefreshProjection");
apiRoute
    .MapGet(
        "/projections/snapshot",
        async ([FromQuery] string name, [FromQuery] bool? unsafeState, [FromServices] IClusterClient client) =>
        {
            try
            {
                var grain = client.GetGrain<IMultiProjectionGrain>(name);
                var rb = await grain.GetSnapshotJsonAsync(unsafeState ?? true);
                if (!rb.IsSuccess) return Results.BadRequest(new { error = rb.GetException()?.Message });
                return Results.Text(rb.GetValue(), "application/json");
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("GetProjectionSnapshot");
apiRoute
    .MapPost(
        "/projections/overwrite-version",
        async ([FromQuery] string name, [FromQuery] string newVersion, [FromServices] IClusterClient client) =>
        {
            try
            {
                var grain = client.GetGrain<IMultiProjectionGrain>(name);
                var ok = await grain.OverwritePersistedStateVersionAsync(newVersion);
                return ok
                    ? Results.Ok(new { success = true })
                    : Results.BadRequest(new { error = "No persisted state to overwrite or invalid envelope" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("OverwriteProjectionPersistedVersion");
apiRoute
    .MapPost(
        "/removeweatherforecast",
        async ([FromBody] DeleteWeatherForecast command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command);
            if (result.IsSuccess)
                return Results.Ok(
                    new
                    {
                        success = true,
                        eventId = result.GetValue().EventId,
                        aggregateId = command.ForecastId,
                        sortableUniqueId = result.GetValue().SortableUniqueId
                    });
            return Results.BadRequest(
                new
                {
                    success = false,
                    error = result.GetException()?.Message
                });
        })
    .WithName("RemoveWeatherForecast");

apiRoute.MapGet("/health", () => Results.Ok("Healthy")).WithName("HealthCheck");
apiRoute
    .MapGet(
        "/orleans/test",
        async ([FromServices] ISekibanExecutor executor, [FromServices] ILogger<Program> logger) =>
        {
            try
            {
                logger.LogInformation("Testing Orleans connectivity...");
                var query = new GetWeatherForecastListQuery();
                var result = await executor.QueryAsync(query);
                if (result.IsSuccess)
                    return Results.Ok(
                        new
                        {
                            status = "Orleans is working",
                            message = "Successfully executed query through Orleans",
                            itemCount = result.GetValue().TotalCount
                        });
                return Results.Ok(
                    new
                    {
                        status = "Orleans query failed",
                        error = result.GetException()?.Message ?? "Unknown error"
                    });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Orleans test failed");
                return Results.Ok(
                    new
                    {
                        status = "Orleans test failed",
                        error = ex.Message
                    });
            }
        })
        .WithName("TestOrleans");
app.MapDefaultEndpoints();
app.Run();
