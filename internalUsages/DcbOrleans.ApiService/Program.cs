using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Dcb.Domain;
using Dcb.Domain.ClassRoom;
using Dcb.Domain.Enrollment;
using Dcb.Domain.Queries;
using Dcb.Domain.Student;
using Dcb.Domain.Weather;
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



// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add Azure Storage clients for Orleans
builder.AddKeyedAzureTableServiceClient("DcbOrleansClusteringTable");
builder.AddKeyedAzureTableServiceClient("DcbOrleansGrainTable");
builder.AddKeyedAzureBlobServiceClient("DcbOrleansGrainState");
builder.AddKeyedAzureQueueServiceClient("DcbOrleansQueue");

// Configure Orleans
builder.UseOrleans(config =>
{
    // Use localhost clustering for development
    config.UseLocalhostClustering();
    
    // Check if we should use in-memory streams (for development/testing)
    var useInMemoryStreams = builder.Configuration.GetValue<bool>("Orleans:UseInMemoryStreams");
    
    if (useInMemoryStreams)
    {
        // Use in-memory streams for development/testing
        config.AddMemoryStreams("EventStreamProvider", configurator =>
        {
            configurator.ConfigurePartitioning(2);
        });
        config.AddMemoryStreams("DcbOrleansQueue", configurator =>
        {
            configurator.ConfigurePartitioning(2);
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
    // Even though Aspire sets configuration via environment variables,
    // we still need to explicitly register the storage providers

    // Default storage using Azure Blob Storage
    config.AddAzureBlobGrainStorageAsDefault(options =>
    {
        options.Configure<IServiceProvider>((opt, sp) =>
        {
            opt.BlobServiceClient = sp.GetKeyedService<BlobServiceClient>("DcbOrleansGrainState");
        });
    });

    // OrleansStorage provider for MultiProjectionGrain
    config.AddAzureBlobGrainStorage(
        "OrleansStorage",
        options =>
        {
            options.Configure<IServiceProvider>((opt, sp) =>
            {
                opt.BlobServiceClient = sp.GetKeyedService<BlobServiceClient>("DcbOrleansGrainState");
            });
        });

    // Additional named storage providers
    config.AddAzureBlobGrainStorage(
        "dcb-orleans-queue",
        options =>
        {
            options.Configure<IServiceProvider>((opt, sp) =>
            {
                opt.BlobServiceClient = sp.GetKeyedService<BlobServiceClient>("DcbOrleansGrainState");
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

    // Add grain storage for PubSub (used by Orleans streaming) - only for Azure Queue Streams
    if (!useInMemoryStreams)
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

    // Add grain storage for the stream provider
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

    // Orleans will automatically discover grains in the same assembly
    config.ConfigureServices(services =>
    {
        services.AddTransient<IGrainStorageSerializer, NewtonsoftJsonDcbOrleansSerializer>();
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
// Snapshot offload: Azure Blob Storage accessor (Azurite in dev)
builder.Services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
{
    // Prefer configuration value, fallback to Azurite dev storage
    var conn = builder.Configuration.GetConnectionString("SekibanAzureStorage") ?? "UseDevelopmentStorage=true";
    var container = builder.Configuration.GetValue<string>("Sekiban:SnapshotContainer") ?? "dcb-snapshots";
    return new AzureBlobStorageSnapshotAccessor(conn, container);
});
// Note: IEventSubscription is now created per-grain via IEventSubscriptionResolver
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
        "/students",
        async ([FromBody] CreateStudent command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command);
            if (result.IsSuccess)
            {
                return Results.Ok(
                    new
                    {
                        studentId = command.StudentId,
                        eventId = result.GetValue().EventId,
                        sortableUniqueId = result.GetValue().SortableUniqueId,
                        message = "Student created successfully"
                    });
            }
            return Results.BadRequest(new { error = result.GetException().Message });
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
            if (result.IsSuccess)
            {
                var queryResult = result.GetValue();
                return Results.Ok(queryResult.Items);
            }
            return Results.BadRequest(new { error = result.GetException().Message });
        })
    .WithOpenApi()
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
    .WithOpenApi()
    .WithName("GetStudent");

// ClassRoom endpoints
apiRoute
    .MapPost(
        "/classrooms",
        async ([FromBody] CreateClassRoom command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command, CreateClassRoomHandler.HandleAsync);
            if (result.IsSuccess)
            {
                return Results.Ok(
                    new
                    {
                        classRoomId = command.ClassRoomId,
                        eventId = result.GetValue().EventId,
                        sortableUniqueId = result.GetValue().SortableUniqueId,
                        message = "ClassRoom created successfully"
                    });
            }
            return Results.BadRequest(new { error = result.GetException().Message });
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
            if (result.IsSuccess)
            {
                var queryResult = result.GetValue();
                return Results.Ok(queryResult.Items);
            }
            return Results.BadRequest(new { error = result.GetException().Message });
        })
    .WithOpenApi()
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
    .WithOpenApi()
    .WithName("GetClassRoom");

// Enrollment endpoints
apiRoute
    .MapPost(
        "/enrollments/add",
        async ([FromBody] EnrollStudentInClassRoom command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command, EnrollStudentInClassRoomHandler.HandleAsync);
            if (result.IsSuccess)
            {
                return Results.Ok(
                    new
                    {
                        studentId = command.StudentId,
                        classRoomId = command.ClassRoomId,
                        eventId = result.GetValue().EventId,
                        sortableUniqueId = result.GetValue().SortableUniqueId,
                        message = "Student enrolled successfully"
                    });
            }
            return Results.BadRequest(new { error = result.GetException().Message });
        })
    .WithOpenApi()
    .WithName("EnrollStudent");

apiRoute
    .MapPost(
        "/enrollments/drop",
        async ([FromBody] DropStudentFromClassRoom command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command, DropStudentFromClassRoomHandler.HandleAsync);
            if (result.IsSuccess)
            {
                return Results.Ok(
                    new
                    {
                        studentId = command.StudentId,
                        classRoomId = command.ClassRoomId,
                        eventId = result.GetValue().EventId,
                        sortableUniqueId = result.GetValue().SortableUniqueId,
                        message = "Student dropped successfully"
                    });
            }
            return Results.BadRequest(new { error = result.GetException().Message });
        })
    .WithOpenApi()
    .WithName("DropStudent");

// Debug endpoint to check database
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
                }
                return Results.BadRequest(new { error = result.GetException()?.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Debug] Exception: {ex}");
                return Results.Problem(detail: ex.Message);
            }
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
                // Return the actual WeatherForecastItem objects for proper deserialization
                return Results.Ok(queryResult.Items);
            }

            return Results.BadRequest(new { error = result.GetException()?.Message });
        })
    .WithOpenApi()
    .WithName("GetWeatherForecast");

apiRoute
    .MapPost(
        "/inputweatherforecast",
        async ([FromBody] CreateWeatherForecast command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command);
            if (result.IsSuccess)
            {
                return Results.Ok(
                    new
                    {
                        success = true,
                        eventId = result.GetValue().EventId,
                        aggregateId = command.ForecastId,
                        sortableUniqueId = result.GetValue().SortableUniqueId
                    });
            }
            return Results.BadRequest(
                new
                {
                    success = false,
                    error = result.GetException()?.Message
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
            if (result.IsSuccess)
            {
                return Results.Ok(
                    new
                    {
                        success = true,
                        eventId = result.GetValue().EventId,
                        aggregateId = command.ForecastId,
                        sortableUniqueId = result.GetValue().SortableUniqueId
                    });
            }
            return Results.BadRequest(
                new
                {
                    success = false,
                    error = result.GetException()?.Message
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
            var result = await executor.QueryAsync(query);

            if (result.IsSuccess)
            {
                var countResult = (WeatherForecastCountResult)result.GetValue();
                return Results.Ok(new
                {
                    totalCount = countResult.TotalCount,
                    safeCount = countResult.SafeCount,
                    unsafeCount = countResult.UnsafeCount,
                    isSafeState = countResult.IsSafeState,
                    lastProcessedEventId = countResult.LastProcessedEventId
                });
            }

            return Results.BadRequest(new { error = result.GetException()?.Message ?? "Query failed" });
        })
    .WithOpenApi()
    .WithName("GetWeatherForecastCount");

apiRoute
    .MapPost(
        "/removeweatherforecast",
        async ([FromBody] DeleteWeatherForecast command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command);
            if (result.IsSuccess)
            {
                return Results.Ok(
                    new
                    {
                        success = true,
                        eventId = result.GetValue().EventId,
                        aggregateId = command.ForecastId,
                        sortableUniqueId = result.GetValue().SortableUniqueId
                    });
            }
            return Results.BadRequest(
                new
                {
                    success = false,
                    error = result.GetException()?.Message
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
        async ([FromServices] ISekibanExecutor executor, [FromServices] ILogger<Program> logger) =>
        {
            try
            {
                logger.LogInformation("Testing Orleans connectivity...");

                // Try a simple query to test Orleans grains
                var query = new GetWeatherForecastListQuery();
                var result = await executor.QueryAsync(query);

                if (result.IsSuccess)
                {
                    return Results.Ok(
                        new
                        {
                            status = "Orleans is working",
                            message = "Successfully executed query through Orleans",
                            itemCount = result.GetValue().TotalCount
                        });
                }

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
    .WithOpenApi()
    .WithName("TestOrleans");

app.MapDefaultEndpoints();

app.Run();
