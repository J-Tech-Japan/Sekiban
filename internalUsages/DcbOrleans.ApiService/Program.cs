using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Storage;
using ResultBoxes;
using Scalar.AspNetCore;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Dcb.Domain;
using Dcb.Domain.Student;
using Dcb.Domain.ClassRoom;
using Dcb.Domain.Enrollment;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add PostgreSQL connection
builder.AddNpgsqlDbContext<SekibanDcbDbContext>("DcbPostgres");

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
    // Add Azure Queue Streams
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
            configurator.Configure<HashRingStreamQueueMapperOptions>(ob =>
                ob.Configure(o => o.TotalQueueCount = 3));

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
            configurator.Configure<HashRingStreamQueueMapperOptions>(ob =>
                ob.Configure(o => o.TotalQueueCount = 3));

            configurator.ConfigurePullingAgent(ob => ob.Configure(opt =>
            {
                opt.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(1000);
                opt.BatchContainerBatchSize = 256;
                opt.StreamInactivityPeriod = TimeSpan.FromMinutes(10);
            }));
            configurator.ConfigureCacheSize(8192);
        });

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
    
    // Additional named storage providers
    config.AddAzureBlobGrainStorage("dcb-orleans-queue", options =>
    {
        options.Configure<IServiceProvider>((opt, sp) =>
        {
            opt.BlobServiceClient = sp.GetKeyedService<BlobServiceClient>("DcbOrleansGrainState");
        });
    });
    
    config.AddAzureTableGrainStorage("DcbOrleansGrainTable", options =>
    {
        options.Configure<IServiceProvider>((opt, sp) =>
        {
            opt.TableServiceClient = sp.GetKeyedService<TableServiceClient>("DcbOrleansGrainTable");
        });
    });
    
    // Add grain storage for PubSubStore (used by Orleans streaming)
    config.AddAzureTableGrainStorage(
        "PubSubStore",
        options =>
        {
            options.Configure<IServiceProvider>((opt, sp) =>
            {
                opt.TableServiceClient = sp.GetKeyedService<TableServiceClient>("DcbOrleansGrainTable");
            });
        });

    // Add grain storage for the stream provider
    config.AddAzureTableGrainStorage(
        "EventStreamProvider",
        options =>
        {
            options.Configure<IServiceProvider>((opt, sp) =>
            {
                opt.TableServiceClient = sp.GetKeyedService<TableServiceClient>("DcbOrleansGrainTable");
            });
        });
});

// Register DCB domain types
var domainTypes = DomainType.GetDomainTypes();
builder.Services.AddSingleton(domainTypes);

// Register command executor
// PubSub: Orleans AllEvents (EventStreamProvider / AllEvents / Guid.Empty)
builder.Services.AddSingleton<IStreamDestinationResolver>(
    sp => new DefaultOrleansStreamDestinationResolver(
        providerName: "EventStreamProvider",
        @namespace: "AllEvents",
        streamId: Guid.Empty));
builder.Services.AddSingleton<IEventPublisher, OrleansEventPublisher>();

builder.Services.AddScoped<ISekibanExecutor>(sp =>
{
    var clusterClient = sp.GetRequiredService<Orleans.IClusterClient>();
    var eventStore = sp.GetRequiredService<IEventStore>();
    var domainTypes = sp.GetRequiredService<DcbDomainTypes>();
    var publisher = sp.GetRequiredService<IEventPublisher>();
    return new OrleansDcbExecutor(clusterClient, eventStore, domainTypes, publisher);
});
builder.Services.AddScoped<ICommandExecutor>(sp => sp.GetRequiredService<ISekibanExecutor>());
builder.Services.AddScoped<IActorObjectAccessor, OrleansActorObjectAccessor>();

// Add PostgreSQL event store
// Note: TagStatePersistent is not needed when using Orleans as Orleans grains have their own persistence
builder.Services.AddSingleton<IEventStore, PostgresEventStore>();
// Register DbContextFactory for PostgresEventStore
builder.Services.AddDbContextFactory<SekibanDcbDbContext>(options =>
{
    // The connection string will be configured by Aspire's AddNpgsqlDbContext above
});

if (builder.Environment.IsDevelopment())
{
    // Add CORS services
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            // Allow any origin in development for easier testing
            policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
        });
    });
}
var app = builder.Build();

// Run database migrations
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<SekibanDcbDbContext>();
    await dbContext.Database.MigrateAsync();
}

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
                return Results.Ok(new { 
                    studentId = command.StudentId, 
                    eventId = result.GetValue().EventId,
                    message = "Student created successfully" 
                });
            }
            return Results.BadRequest(new { error = result.GetException().Message });
        })
    .WithOpenApi()
    .WithName("CreateStudent");

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
                return Results.Ok(new
                {
                    studentId = studentId,
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
                return Results.Ok(new { 
                    classRoomId = command.ClassRoomId, 
                    eventId = result.GetValue().EventId,
                    message = "ClassRoom created successfully" 
                });
            }
            return Results.BadRequest(new { error = result.GetException().Message });
        })
    .WithOpenApi()
    .WithName("CreateClassRoom");

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
                return Results.Ok(new
                {
                    classRoomId = classRoomId,
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
        "/enrollments",
        async ([FromBody] EnrollStudentInClassRoom command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command, EnrollStudentInClassRoomHandler.HandleAsync);
            if (result.IsSuccess)
            {
                return Results.Ok(new { 
                    studentId = command.StudentId,
                    classRoomId = command.ClassRoomId,
                    eventId = result.GetValue().EventId,
                    message = "Student enrolled successfully" 
                });
            }
            return Results.BadRequest(new { error = result.GetException().Message });
        })
    .WithOpenApi()
    .WithName("EnrollStudent");

apiRoute
    .MapPost(
        "/drop",
        async ([FromBody] DropStudentFromClassRoom command, [FromServices] ISekibanExecutor executor) =>
        {
            var result = await executor.ExecuteAsync(command, DropStudentFromClassRoomHandler.HandleAsync);
            if (result.IsSuccess)
            {
                return Results.Ok(new { 
                    studentId = command.StudentId,
                    classRoomId = command.ClassRoomId,
                    eventId = result.GetValue().EventId,
                    message = "Student dropped successfully" 
                });
            }
            return Results.BadRequest(new { error = result.GetException().Message });
        })
    .WithOpenApi()
    .WithName("DropStudent");

// Health check endpoint
apiRoute.MapGet("/health", () => Results.Ok("Healthy"))
    .WithOpenApi()
    .WithName("HealthCheck");

app.MapDefaultEndpoints();

app.Run();