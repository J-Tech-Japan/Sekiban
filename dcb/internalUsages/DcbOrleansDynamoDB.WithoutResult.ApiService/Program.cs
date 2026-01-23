using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.ClassRoom;
using Dcb.Domain.WithoutResult.Enrollment;
using Dcb.Domain.WithoutResult.Queries;
using Dcb.Domain.WithoutResult.Student;
using Dcb.Domain.WithoutResult.Weather;
using DcbOrleansDynamoDB.WithoutResult.ApiService.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Orleans.Storage;
using Scalar.AspNetCore;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.BlobStorage.S3;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
var builder = WebApplication.CreateBuilder(args);

// Configure logging to suppress noisy AWS SDK logs in development
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddFilter("Amazon", LogLevel.Error);
    builder.Logging.AddFilter("Amazon.Runtime", LogLevel.Error);
}

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Add global exception handler
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();



// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var databaseType = builder.Configuration.GetSection("Sekiban").GetValue<string>("Database")?.ToLower() ?? "dynamodb";
var useInMemoryStreams = builder.Configuration.GetValue<bool>("Orleans:UseInMemoryStreams", true);

// Determine if running locally (Aspire/LocalStack) vs in AWS
// Only check DynamoDb:ServiceUrl - this is set when using LocalStack
// Do NOT rely on IsDevelopment() as AWS "dev" environments still use ASPNETCORE_ENVIRONMENT=Development
var isLocalDevelopment = !string.IsNullOrEmpty(builder.Configuration["DynamoDb:ServiceUrl"]);

// In AWS deployment, we use RDS for Orleans (SQS streams not yet available due to SDK version conflict)
// In local development, we use in-memory everything
if (!isLocalDevelopment && !useInMemoryStreams)
{
    // AWS Deployment mode - will use RDS for Orleans clustering/state
    // Note: SQS streaming is not yet available, so we still use in-memory streams
    // This is valid for production deployment
}
else if (isLocalDevelopment && !useInMemoryStreams)
{
    // Local mode but trying to use non-memory streams - not supported
    throw new InvalidOperationException(
        "Orleans:UseInMemoryStreams must be true for local development (LocalStack).");
}

// Configure Orleans
builder.UseOrleans(config =>
{
    if (isLocalDevelopment)
    {
        // Local development: use localhost clustering
        config.UseLocalhostClustering();
    }
    else
    {
        // AWS deployment: use AdoNet clustering with RDS PostgreSQL
        // Try to construct connection string from individual RDS secret fields (AWS ECS)
        var rdsHost = builder.Configuration["RDS_HOST"];
        var rdsPort = builder.Configuration["RDS_PORT"] ?? "5432";
        var rdsUsername = builder.Configuration["RDS_USERNAME"];
        var rdsPassword = builder.Configuration["RDS_PASSWORD"];
        var rdsDatabase = builder.Configuration["RDS_DATABASE"];

        string? rdsConnectionString;
        if (!string.IsNullOrEmpty(rdsHost) && !string.IsNullOrEmpty(rdsUsername))
        {
            // Construct PostgreSQL connection string from individual fields
            rdsConnectionString = $"Host={rdsHost};Port={rdsPort};Database={rdsDatabase};Username={rdsUsername};Password={rdsPassword}";
        }
        else
        {
            // Fall back to direct connection string configuration
            rdsConnectionString = builder.Configuration["RdsConnectionString"] ??
                                  builder.Configuration.GetConnectionString("Orleans");
        }

        if (!string.IsNullOrEmpty(rdsConnectionString))
        {
            config.UseAdoNetClustering(options =>
            {
                options.ConnectionString = rdsConnectionString;
                options.Invariant = "Npgsql";
            });

            config.AddAdoNetGrainStorage("OrleansStorage", options =>
            {
                options.ConnectionString = rdsConnectionString;
                options.Invariant = "Npgsql";
            });

            config.AddAdoNetGrainStorage("PubSubStore", options =>
            {
                options.ConnectionString = rdsConnectionString;
                options.Invariant = "Npgsql";
            });

            config.UseAdoNetReminderService(options =>
            {
                options.ConnectionString = rdsConnectionString;
                options.Invariant = "Npgsql";
            });
        }

        // Configure Orleans endpoint for ECS
        var siloPort = builder.Configuration.GetValue<int>("Orleans:SiloPort", 11111);
        var gatewayPort = builder.Configuration.GetValue<int>("Orleans:GatewayPort", 30000);

        config.Configure<Orleans.Configuration.EndpointOptions>(options =>
        {
            options.SiloPort = siloPort;
            options.GatewayPort = gatewayPort;
            // In ECS Fargate, we need to get the private IP
            options.AdvertisedIPAddress = GetPrivateIpAddress();
        });
    }

    // Configure streams
    // Note: SQS streaming is not yet available due to AWS SDK version conflict
    // (Orleans SQS 9.2.1 requires AWS SDK v3, but Sekiban DynamoDB uses AWS SDK v4)
    // For now, use in-memory streams for both local and AWS deployment
    // TODO: Add SQS support when Orleans SQS package supports AWS SDK v4
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

    // Memory grain storage (used for both local and AWS until SQS is available)
    if (isLocalDevelopment || useInMemoryStreams)
    {
        config.AddMemoryGrainStorageAsDefault();
        config.AddMemoryGrainStorage("OrleansStorage");
        config.AddMemoryGrainStorage("dcb-orleans-queue");
        config.AddMemoryGrainStorage("DcbOrleansGrainTable");
        config.AddMemoryGrainStorage("EventStreamProvider");
        config.AddMemoryGrainStorage("PubSubStore");
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
            EnableDynamicSafeWindow = databaseType != "sqlite" && !useInMemoryStreams,
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
if (databaseType != "dynamodb")
{
    throw new InvalidOperationException(
        $"Unsupported Sekiban:Database '{databaseType}'. " +
        "This AWS-focused ApiService only supports 'dynamodb'.");
}

builder.Services.AddSekibanDcbDynamoDbWithAspire();
builder.Services.AddSingleton<IMultiProjectionStateStore, DynamoMultiProjectionStateStore>();
builder.Services.AddSekibanDcbS3BlobStorage(builder.Configuration);

builder.Services.AddTransient<IGrainStorageSerializer, NewtonsoftJsonDcbOrleansSerializer>();
builder.Services.AddTransient<NewtonsoftJsonDcbOrleansSerializer>();
builder.Services.AddSingleton<IStreamDestinationResolver>(sp =>
    new DefaultOrleansStreamDestinationResolver("EventStreamProvider", "AllEvents", Guid.Empty));
builder.Services.AddSingleton<IEventSubscriptionResolver>(sp =>
    new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
builder.Services.AddSingleton<IEventPublisher, OrleansEventPublisher>();
// Note: IEventSubscription is now created per-grain via IEventSubscriptionResolver
builder.Services.AddTransient<ISekibanExecutor, OrleansDcbExecutor>();
builder.Services.AddScoped<IActorObjectAccessor, OrleansActorObjectAccessor>();

// Note: TagStatePersistent is not needed when using Orleans as Orleans grains have their own persistence
// IEventStore is already registered via AddSekibanDcbDynamoDbWithAspire

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

// DynamoDB tables will be created automatically by DynamoDbInitializer

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

// Helper method to get private IP address for ECS Fargate
static System.Net.IPAddress GetPrivateIpAddress()
{
    // In ECS Fargate, the task gets a private IP from the VPC
    // We can use ECS metadata endpoint or enumerate network interfaces
    var ecsMetadataUri = Environment.GetEnvironmentVariable("ECS_CONTAINER_METADATA_URI_V4");

    if (!string.IsNullOrEmpty(ecsMetadataUri))
    {
        try
        {
            using var client = new System.Net.Http.HttpClient();
            var response = client.GetStringAsync($"{ecsMetadataUri}/task").Result;
            // Parse the response to get the IP address
            // For simplicity, fall back to DNS-based resolution
        }
        catch
        {
            // Fall through to DNS-based resolution
        }
    }

    // Fall back to getting the first non-loopback IPv4 address
    var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
    foreach (var ip in host.AddressList)
    {
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
            !System.Net.IPAddress.IsLoopback(ip))
        {
            return ip;
        }
    }

    // Last resort: return loopback
    return System.Net.IPAddress.Loopback;
}
