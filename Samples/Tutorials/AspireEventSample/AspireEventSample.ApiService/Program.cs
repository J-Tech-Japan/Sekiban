using AspireEventSample.ApiService;
using AspireEventSample.ApiService.Endpoints;
using AspireEventSample.ApiService.Grains;
using AspireEventSample.ApiService.ReadModel;
using AspireEventSample.Domain.Aggregates.Branches;
using AspireEventSample.Domain.Generated;
using AspireEventSample.Domain.Projections;
using AspireEventSample.ReadModels;
using Azure.Data.Tables;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orleans.Configuration;
using Orleans.Storage;
using Scalar.AspNetCore;
using Sekiban.Pure;
using Sekiban.Pure.AspNetCore;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.CosmosDb;
using Sekiban.Pure.Orleans;
using Sekiban.Pure.Orleans.Parts;
using Sekiban.Pure.Postgres;
using Sekiban.Pure.Projectors;
using System.Net;
var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// orleans related integrations
builder.AddKeyedAzureTableClient("OrleansSekibanClustering");
builder.AddKeyedAzureTableClient("OrleansSekibanGrainTable");
builder.AddKeyedAzureBlobClient("OrleansSekibanGrainState");
builder.AddKeyedAzureQueueClient("OrleansSekibanQueue");
builder.UseOrleans(config =>
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
    if ((builder.Configuration["ORLEANS_GRAIN_DEFAULT_TYPE"] ?? "").ToLower() == "cosmos")
    {
        config.AddCosmosGrainStorageAsDefault(options =>
        {
            var connectionString = builder.Configuration.GetConnectionString("OrleansCosmos") ??
                throw new InvalidOperationException();
            options.ConfigureCosmosClient(connectionString);
            options.IsResourceCreationEnabled = true;
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

    // config.UseDashboard(options => { });

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
                    cp.ConfigureTableServiceClient(builder.Configuration.GetConnectionString("OrleansSekibanTable"));
                }));
            });
        config.AddEventHubStreams(
            "OrleansSekibanQueue",
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
                    cp.ConfigureTableServiceClient(builder.Configuration.GetConnectionString("OrleansSekibanTable"));
                }));

                // …your cache, queue‑mapper, pulling‑agent settings remain unchanged …
            });
    } else
    {
        config.AddAzureQueueStreams(
            "EventStreamProvider",
            configurator =>
            {
                configurator.ConfigureAzureQueue(options =>
                {
                    options.Configure<IServiceProvider>((queueOptions, sp) =>
                    {
                        queueOptions.QueueServiceClient = sp.GetKeyedService<QueueServiceClient>("OrleansSekibanQueue");
                        queueOptions.QueueNames =
                        [
                            "ywnh5ws65snztguqv8zfa3raz-eventstreamprovider-0",
                            "ywnh5ws65snztguqv8zfa3raz-eventstreamprovider-1",
                            "ywnh5ws65snztguqv8zfa3raz-eventstreamprovider-2"
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
            "OrleansSekibanQueue",
            configurator =>
            {
                configurator.ConfigureAzureQueue(options =>
                {
                    options.Configure<IServiceProvider>((queueOptions, sp) =>
                    {
                        queueOptions.QueueServiceClient = sp.GetKeyedService<QueueServiceClient>("OrleansSekibanQueue");
                        queueOptions.QueueNames =
                        [
                            "ywnh5ws65snztguqv8zfa3raz-orleanssekibanqueue-0",
                            "ywnh5ws65snztguqv8zfa3raz-orleanssekibanqueue-1",
                            "ywnh5ws65snztguqv8zfa3raz-orleanssekibanqueue-2"
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
    }
    if ((builder.Configuration["ORLEANS_GRAIN_DEFAULT_TYPE"] ?? "").ToLower() == "cosmos")
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
        config.AddCosmosGrainStorage(
            "EventStreamProvider",
            options =>
            {
                var connectionString = builder.Configuration.GetConnectionString("OrleansCosmos") ??
                    throw new InvalidOperationException();
                options.ConfigureCosmosClient(connectionString);
                options.IsResourceCreationEnabled = true;
            });
    } else
    {
        config.AddAzureTableGrainStorage(
            "PubSubStore",
            options =>
            {
                options.Configure<IServiceProvider>((opt, sp) =>
                {
                    opt.TableServiceClient = sp.GetKeyedService<TableServiceClient>("OrleansSekibanGrainTable");
                    opt.GrainStorageSerializer = sp.GetRequiredService<NewtonsoftJsonSekibanOrleansSerializer>();
                });
                options.Configure<IGrainStorageSerializer>((op, serializer) => op.GrainStorageSerializer = serializer);
            });

        // Add grain storage for the stream provider
        config.AddAzureTableGrainStorage(
            "EventStreamProvider",
            options =>
            {
                options.Configure<IServiceProvider>((opt, sp) =>
                {
                    opt.TableServiceClient = sp.GetKeyedService<TableServiceClient>("OrleansSekibanGrainTable");
                    opt.GrainStorageSerializer = sp.GetRequiredService<NewtonsoftJsonSekibanOrleansSerializer>();
                });
                options.Configure<IGrainStorageSerializer>((op, serializer) => op.GrainStorageSerializer = serializer);
            });
        // Orleans will automatically discover grains in the same assembly
        config.ConfigureServices(services =>
            services.AddTransient<IGrainStorageSerializer, NewtonsoftJsonSekibanOrleansSerializer>());

    }
    // Orleans will automatically discover grains in the same assembly
    config.ConfigureServices(services =>
        services.AddTransient<IGrainStorageSerializer, NewtonsoftJsonSekibanOrleansSerializer>());
});

// Configure ReadModel Postgres database
var readModelConnectionString = builder.Configuration.GetConnectionString("ReadModel");
builder.Services.AddDbContext<BranchDbContext>(options =>
    options.UseNpgsql(
        readModelConnectionString,
        b => b.MigrationsAssembly("AspireEventSample.MigrationHost")));

// Register the Postgres writer grains
builder.Services.AddTransient<BranchPostgresReadModelAccessor>();
builder.Services
    .AddTransient<IBranchEntityPostgresReadModelAccessorGrain, BranchPostgresReadModelAccessorGrain>();
builder.Services.AddTransient<ICartEntityPostgresWriter, CartEntityPostgresWriterGrain>();
builder.Services.AddTransient<ICartItemEntityPostgresWriterGrain, CartItemEntityPostgresWriterGrain>();

// Register the DatabaseInitializer
builder.Services.AddTransient<DatabaseInitializer>();

// Register ReadModel components
builder.Services.AddSingleton<IEventContextProvider, EventContextProvider>();

// Register entity writers
builder.Services.AddTransient<BranchPostgresReadModelAccessor>();
builder.Services.AddTransient<CartEntityPostgresWriterGrain>();
builder.Services.AddTransient<CartItemEntityPostgresWriterGrain>();

// source generator serialization options
builder.Services.AddSingleton(
    AspireEventSampleDomainDomainTypes.Generate(AspireEventSampleDomainEventsJsonContext.Default.Options));
// general json serializer options
// builder.Services.AddSingleton(AspireEventSampleApiServiceDomainTypes.Generate());
SekibanSerializationTypesChecker.CheckDomainSerializability(
    AspireEventSampleDomainDomainTypes.Generate(AspireEventSampleDomainEventsJsonContext.Default.Options));

builder.Services.AddTransient<ICommandMetadataProvider, CommandMetadataProvider>();
builder.Services.AddTransient<IExecutingUserProvider, HttpExecutingUserProvider>();
builder.Services.AddHttpContextAccessor();

// Add Orleans serialization support
builder.Services.AddTransient<NewtonsoftJsonSekibanOrleansSerializer>();

builder.Services.AddTransient<SekibanOrleansExecutor>();

if (builder.Configuration.GetSection("Sekiban").GetValue<string>("Database")?.ToLower() == "cosmos")
{
    // Cosmos settings
    builder.AddSekibanCosmosDb();
} else
{
    // Postgres settings
    builder.AddSekibanPostgresDb();
}
var app = builder.Build();

// Apply migrations and initialize the database
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BranchDbContext>();
    app.Logger.LogInformation("Applying database migrations...");
    await dbContext.Database.MigrateAsync();
    app.Logger.LogInformation("Database migrations applied successfully.");

    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();
}

// Configure the HTTP request pipeline.
app.UseExceptionHandler();


var apiRoute = app
    .MapGroup("/api")
    .AddEndpointFilter<ExceptionEndpointFilter>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

string[] summaries =
    ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

app
    .MapGet(
        "/weatherforecast",
        () =>
        {
            var forecast = Enumerable
                .Range(1, 5)
                .Select(index =>
                    new WeatherForecast(
                        DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                        Random.Shared.Next(-20, 55),
                        summaries[Random.Shared.Next(summaries.Length)]
                    ))
                .ToArray();
            return forecast;
        })
    .WithName("GetWeatherForecast");

apiRoute
    .MapGet(
        "/getMultiProjection",
        async ([FromServices] IClusterClient clusterClient, [FromServices] SekibanDomainTypes sekibanDomainTypes) =>
        {
            var multiProjectorGrain
                = clusterClient.GetGrain<IMultiProjectorGrain>(BranchMultiProjector.GetMultiProjectorName());
            var state = await multiProjectorGrain.GetStateAsync();
            return sekibanDomainTypes.MultiProjectorsType.ToTypedState(state);
        })
    .WithName("GetMultiProjection")
    .WithOpenApi();

apiRoute
    .MapGet(
        "/branchProjectionWithAggregate",
        async ([FromServices] IClusterClient clusterClient, [FromServices] SekibanDomainTypes sekibanDomainTypes) =>
        {
            var multiProjectorGrain
                = clusterClient.GetGrain<IMultiProjectorGrain>(
                    AggregateListProjector<BranchProjector>.GetMultiProjectorName());
            var state = await multiProjectorGrain.GetStateAsync();
            return sekibanDomainTypes.MultiProjectorsType.ToTypedState(state);
        })
    .WithName("branchProjectionWithAggregate")
    .WithDescription(
        "This is failing due to no serializer of ListQueryResult AggregateListProjector<BranchProjector>. Can Still use query")
    .WithOpenApi();

app.MapDefaultEndpoints();

// Register domain endpoints
apiRoute.MapBranchEndpoints();
apiRoute.MapCartEndpoints();

app.Run();