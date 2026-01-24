using Microsoft.Extensions.DependencyInjection;
using Dcb.EventSource;
using Microsoft.AspNetCore.Mvc;
using Orleans.Configuration;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Storage;
using Scalar.AspNetCore;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.BlobStorage.S3;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Snapshots;
using SekibanDcbDeciderAws.ApiService;
using SekibanDcbDeciderAws.ApiService.Endpoints;
using SekibanDcbDeciderAws.ApiService.Auth;
using SekibanDcbDeciderAws.ApiService.Exceptions;
using SekibanDcbDeciderAws.ApiService.Realtime;

var builder = WebApplication.CreateBuilder(args);

// Configure logging to suppress AWS SDK warnings in development
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

// Add Authentication & Identity
// Get the PostgreSQL connection string from Aspire configuration (separate database for Identity)
var authConnectionString = builder.Configuration.GetConnectionString("IdentityPostgres");
if (!string.IsNullOrEmpty(authConnectionString))
{
    builder.Services.AddAuthServices(builder.Configuration, authConnectionString);
    // Add background service to initialize auth database and seed users
    builder.Services.AddHostedService<AuthDbInitializer>();
}

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var databaseType = builder.Configuration.GetSection("Sekiban").GetValue<string>("Database")?.ToLower() ?? "dynamodb";
var useInMemoryStreams = builder.Configuration.GetValue<bool>("Orleans:UseInMemoryStreams", true);

// Determine if running locally (Aspire/LocalStack) vs in AWS
var isLocalDevelopment = !string.IsNullOrEmpty(builder.Configuration["DynamoDb:ServiceUrl"]);

// Build RDS connection string early (needed for schema init and Orleans config)
string? rdsConnectionString = null;
if (!isLocalDevelopment)
{
    var rdsHost = builder.Configuration["RDS_HOST"];
    var rdsPort = builder.Configuration["RDS_PORT"] ?? "5432";
    var rdsUsername = builder.Configuration["RDS_USERNAME"];
    var rdsPassword = builder.Configuration["RDS_PASSWORD"];
    var rdsDatabase = builder.Configuration["RDS_DATABASE"];

    if (!string.IsNullOrEmpty(rdsHost) && !string.IsNullOrEmpty(rdsUsername))
    {
        rdsConnectionString = $"Host={rdsHost};Port={rdsPort};Database={rdsDatabase};Username={rdsUsername};Password={rdsPassword}";
    }
    else
    {
        rdsConnectionString = builder.Configuration["RdsConnectionString"] ??
                              builder.Configuration.GetConnectionString("Orleans");
    }

    // Initialize Orleans schema if RDS is configured
    if (!string.IsNullOrEmpty(rdsConnectionString))
    {
        Console.WriteLine("Initializing Orleans PostgreSQL schema...");
        var schemaInitializer = new OrleansSchemaInitializer(
            LoggerFactory.Create(b => b.AddConsole()).CreateLogger<OrleansSchemaInitializer>());
        schemaInitializer.InitializeAsync(rdsConnectionString).GetAwaiter().GetResult();
    }
}

// Configure Orleans
builder.UseOrleans(config =>
{
    if (isLocalDevelopment)
    {
        config.UseLocalhostClustering();
    }
    else
    {
        // AWS deployment: use AdoNet clustering with RDS PostgreSQL
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

        // Configure Orleans endpoint for ECS
        var siloPort = builder.Configuration.GetValue<int>("Orleans:SiloPort", 11111);
        var gatewayPort = builder.Configuration.GetValue<int>("Orleans:GatewayPort", 30000);

        config.Configure<Orleans.Configuration.EndpointOptions>(options =>
        {
            options.SiloPort = siloPort;
            options.GatewayPort = gatewayPort;
            options.AdvertisedIPAddress = GetPrivateIpAddress();
        });
    }

    // Use in-memory streams for development/testing with enhanced configuration
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
            SafeWindowMs = 20000,
            EnableDynamicSafeWindow = !useInMemoryStreams,
            MaxExtraSafeWindowMs = 30000,
            LagEmaAlpha = 0.3,
            LagDecayPerSecond = 0.98
        };
        services.AddTransient<GeneralMultiProjectionActorOptions>(_ => dynamicOptions);
    });
});

var domainTypes = DomainType.GetDomainTypes();
builder.Services.AddSingleton(domainTypes);
builder.Services.AddSingleton<SseTopicHub>();
builder.Services.AddHostedService<OrleansStreamEventRouter>();

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
builder.Services.AddTransient<ISekibanExecutor, OrleansDcbExecutor>();
builder.Services.AddScoped<IActorObjectAccessor, OrleansActorObjectAccessor>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        });
    });
}
var app = builder.Build();

// Log startup configuration
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
startupLogger.LogInformation("Database type: {DatabaseType}", databaseType);

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

// Use authentication & authorization middleware (if configured)
if (!string.IsNullOrEmpty(authConnectionString))
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapAuthEndpoints();
}

// Map all endpoints
apiRoute.MapStudentEndpoints();
apiRoute.MapClassRoomEndpoints();
apiRoute.MapEnrollmentEndpoints();
apiRoute.MapWeatherEndpoints();
apiRoute.MapProjectionEndpoints();
apiRoute.MapDebugEndpoints();

// MeetingRoom endpoints
apiRoute.MapRoomEndpoints();
apiRoute.MapReservationEndpoints();
apiRoute.MapApprovalEndpoints();
apiRoute.MapUserDirectoryEndpoints();
apiRoute.MapStreamEndpoints();

// Test data endpoints
apiRoute.MapTestDataEndpoints();

app.MapDefaultEndpoints();

app.Run();

// Helper method to get private IP address for ECS Fargate
static System.Net.IPAddress GetPrivateIpAddress()
{
    var ecsMetadataUri = Environment.GetEnvironmentVariable("ECS_CONTAINER_METADATA_URI_V4");

    if (!string.IsNullOrEmpty(ecsMetadataUri))
    {
        try
        {
            using var client = new System.Net.Http.HttpClient();
            var response = client.GetStringAsync($"{ecsMetadataUri}/task").Result;
        }
        catch { }
    }

    var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
    foreach (var ip in host.AddressList)
    {
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
            !System.Net.IPAddress.IsLoopback(ip))
        {
            return ip;
        }
    }

    return System.Net.IPAddress.Loopback;
}
