using System.Net;
using Azure.Data.Tables;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Mvc;
using Orleans.Configuration;
using Orleans.Storage;
using OrleansSekiban.Domain;
using OrleansSekiban.Domain.Aggregates.WeatherForecasts.Commands;
using OrleansSekiban.Domain.Aggregates.WeatherForecasts.Queries;
using OrleansSekiban.Domain.Generated;
using OrleansSekiban.Domain.Projections.Count;
using ResultBoxes;
using Scalar.AspNetCore;
using Sekiban.Pure.AspNetCore;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.CosmosDb;
using Sekiban.Pure.Orleans;
using Sekiban.Pure.Orleans.Parts;
using Sekiban.Pure.Postgres;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
builder.Services.AddApplicationInsightsTelemetry();
// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

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
        config.AddCosmosGrainStorageAsDefault(options =>
        {
            var connectionString = builder.Configuration.GetConnectionString("OrleansCosmos") ??
                                   throw new InvalidOperationException();
            options.ConfigureCosmosClient(connectionString);
            options.IsResourceCreationEnabled = true;
        });


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
                    cp.ConfigureTableServiceClient(
                        builder.Configuration.GetConnectionString("OrleansSekibanTable"));
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
                    cp.ConfigureTableServiceClient(
                        builder.Configuration.GetConnectionString("OrleansSekibanTable"));
                }));

                // …your cache, queue‑mapper, pulling‑agent settings remain unchanged …
            });
    }
    else
    {
        config.AddAzureQueueStreams("EventStreamProvider", configurator =>
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
                ob.Configure(o => o.TotalQueueCount = 3)); // 8 → 3 へ

            // --- Pulling Agent の頻度・バッチ ---
            configurator.ConfigurePullingAgent(ob =>
                ob.Configure(opt =>
                {
                    opt.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(1000);
                    opt.BatchContainerBatchSize = 256;
                    opt.StreamInactivityPeriod = TimeSpan.FromMinutes(10);
                }));
            // --- キャッシュ ---
            configurator.ConfigureCacheSize(8192);
        });
        config.AddAzureQueueStreams("OrleansSekibanQueue", configurator =>
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
                ob.Configure(o => o.TotalQueueCount = 3)); // 8 → 3 へ

            // --- Pulling Agent の頻度・バッチ ---
            configurator.ConfigurePullingAgent(ob =>
                ob.Configure(opt =>
                {
                    opt.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(1000);
                    opt.BatchContainerBatchSize = 256;
                    opt.StreamInactivityPeriod = TimeSpan.FromMinutes(10);
                }));
            // --- キャッシュ ---
            configurator.ConfigureCacheSize(8192);
        });
    }

    if ((builder.Configuration["ORLEANS_GRAIN_DEFAULT_TYPE"] ?? "").ToLower() == "cosmos")
    {
        config.AddCosmosGrainStorage("PubSubStore", options =>
        {
            var connectionString = builder.Configuration.GetConnectionString("OrleansCosmos") ??
                                   throw new InvalidOperationException();
            options.ConfigureCosmosClient(connectionString);
            options.IsResourceCreationEnabled = true;
        });
        config.AddCosmosGrainStorage("EventStreamProvider", options =>
        {
            var connectionString = builder.Configuration.GetConnectionString("OrleansCosmos") ??
                                   throw new InvalidOperationException();
            options.ConfigureCosmosClient(connectionString);
            options.IsResourceCreationEnabled = true;
        });
    }
    else
    {
        config.AddAzureTableGrainStorage("PubSubStore", options =>
        {
            options.Configure<IServiceProvider>((opt, sp) =>
            {
                opt.TableServiceClient = sp.GetKeyedService<TableServiceClient>("OrleansSekibanGrainTable");
                opt.GrainStorageSerializer = sp.GetRequiredService<NewtonsoftJsonSekibanOrleansSerializer>();
            });
            options.Configure<IGrainStorageSerializer>((op, serializer) => op.GrainStorageSerializer = serializer);
        });

        // Add grain storage for the stream provider
        config.AddAzureTableGrainStorage("EventStreamProvider", options =>
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

builder.Services.AddSingleton(
    OrleansSekibanDomainDomainTypes.Generate(OrleansSekibanDomainEventsJsonContext.Default.Options));

SekibanSerializationTypesChecker.CheckDomainSerializability(
    OrleansSekibanDomainDomainTypes.Generate(OrleansSekibanDomainEventsJsonContext.Default.Options));

builder.Services.AddTransient<ICommandMetadataProvider, CommandMetadataProvider>();
builder.Services.AddTransient<IExecutingUserProvider, HttpExecutingUserProvider>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<IGrainStorageSerializer, NewtonsoftJsonSekibanOrleansSerializer>();
builder.Services.AddTransient<NewtonsoftJsonSekibanOrleansSerializer>();
builder.Services.AddTransient<SekibanOrleansExecutor>();


if (builder.Configuration.GetSection("Sekiban").GetValue<string>("Database")?.ToLower() == "cosmos")
    // Cosmos settings
    builder.AddSekibanCosmosDb();
else
    // Postgres settings
    builder.AddSekibanPostgresDb();
// Add CORS services and configure a policy that allows all origins
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

var apiRoute = app
    .MapGroup("/api")
    .AddEndpointFilter<ExceptionEndpointFilter>();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Use CORS middleware (must be called before other middleware that sends responses)
app.UseCors();

string[] summaries =
    ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

apiRoute.MapGet("/weatherforecast", 
        async ([FromQuery] string? waitForSortableUniqueId, [FromServices] SekibanOrleansExecutor executor) =>
        {
            var query = new WeatherForecastQuery("")
            {
                WaitForSortableUniqueId = waitForSortableUniqueId
            };
            var list = await executor.QueryAsync(query).UnwrapBox();
            return list.Items;
        })
    .WithOpenApi()
    .WithName("GetWeatherForecast");

apiRoute
    .MapPost(
        "/inputweatherforecast",
        async (
                [FromBody] InputWeatherForecastCommand command,
                [FromServices] SekibanOrleansExecutor executor) =>
            await executor.CommandAsync(command).ToSimpleCommandResponse().UnwrapBox())
    .WithName("InputWeatherForecast")
    .WithOpenApi();

apiRoute
    .MapPost(
        "/removeweatherforecast",
        async (
                [FromBody] RemoveWeatherForecastCommand command,
                [FromServices] SekibanOrleansExecutor executor) =>
            await executor.CommandAsync(command).ToSimpleCommandResponse().UnwrapBox())
    .WithName("RemoveWeatherForecast")
    .WithOpenApi();

apiRoute
    .MapPost(
        "/updateweatherforecastlocation",
        async (
                [FromBody] UpdateWeatherForecastLocationCommand command,
                [FromServices] SekibanOrleansExecutor executor) =>
            await executor.CommandAsync(command).ToSimpleCommandResponse().UnwrapBox())
    .WithName("UpdateWeatherForecastLocation")
    .WithOpenApi();

apiRoute.MapGet("/weatherCountByLocation/{location}",
        async ([FromRoute] string location, [FromServices] SekibanOrleansExecutor executor) =>
        await executor.QueryAsync(new WeatherCountQuery(location)).UnwrapBox()).WithOpenApi()
    .WithName("GetWeatherCountByLocation")
    .WithDescription("Get the count of weather forecasts by location");
app.MapDefaultEndpoints();

app.Run();