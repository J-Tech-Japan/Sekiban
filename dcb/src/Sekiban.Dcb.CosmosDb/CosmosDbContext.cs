using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Context for managing CosmosDB containers
/// </summary>
public class CosmosDbContext : IDisposable
{
    private static readonly Action<ILogger, Exception?> LogInitializingConnection =
        LoggerMessage.Define(LogLevel.Information, new EventId(1, nameof(LogInitializingConnection)), "Initializing CosmosDB connection");

    private static readonly Action<ILogger, string, Exception?> LogUsingDatabase =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(2, nameof(LogUsingDatabase)), "Using CosmosDB database: {DatabaseName}");

    private static readonly Action<ILogger, Exception?> LogEventsContainerInitialized =
        LoggerMessage.Define(LogLevel.Information, new EventId(3, nameof(LogEventsContainerInitialized)), "Events container initialized");

    private static readonly Action<ILogger, Exception?> LogTagsContainerInitialized =
        LoggerMessage.Define(LogLevel.Information, new EventId(4, nameof(LogTagsContainerInitialized)), "Tags container initialized");

    private readonly string? _connectionString;
    private readonly string _databaseName;
    private readonly ILogger<CosmosDbContext>? _logger;
    private readonly CosmosDbEventStoreOptions _options;
    private Container? _eventsContainer;
    private Container? _tagsContainer;
    private Container? _multiProjectionStatesContainer;
    private CosmosClient? _cosmosClient;
    private Database? _database;
    private bool _disposed;
    private readonly bool _ownsCosmosClient;

    /// <summary>
    ///     Constructor from configuration (deprecated - use extension methods instead)
    /// </summary>
    [Obsolete("Use SekibanDcbCosmosDbExtensions.AddSekibanDcbCosmosDb instead")]
    public CosmosDbContext(IConfiguration configuration, ILogger<CosmosDbContext>? logger = null, CosmosDbEventStoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _logger = logger;
        _options = options ?? new CosmosDbEventStoreOptions();
        // Try multiple connection string keys for backward compatibility
        _connectionString = configuration.GetConnectionString("SekibanDcbCosmos")
            ?? configuration.GetConnectionString("SekibanDcbCosmosDb")
            ?? configuration.GetConnectionString("CosmosDb")
            ?? configuration.GetConnectionString("cosmosdb")
            ?? throw new InvalidOperationException(
                "No CosmosDB connection string found. Configure a connection string in " +
                "'ConnectionStrings:SekibanDcbCosmos', 'ConnectionStrings:SekibanDcbCosmosDb', " +
                "'ConnectionStrings:CosmosDb', or 'ConnectionStrings:cosmosdb'");
        _databaseName = configuration["CosmosDb:DatabaseName"] ?? "SekibanDcb";
        _ownsCosmosClient = true;
    }

    /// <summary>
    ///     Constructor with connection string and database name.
    /// </summary>
    public CosmosDbContext(
        string connectionString,
        string databaseName = "SekibanDcb",
        ILogger<CosmosDbContext>? logger = null,
        CosmosDbEventStoreOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new CosmosDbEventStoreOptions();
        _connectionString = connectionString;
        _databaseName = databaseName;
        _ownsCosmosClient = true;
    }

    /// <summary>
    ///     Constructor that accepts an existing CosmosClient (for Aspire)
    /// </summary>
    public CosmosDbContext(
        CosmosClient cosmosClient,
        string databaseName = "SekibanDcb",
        ILogger<CosmosDbContext>? logger = null,
        CosmosDbEventStoreOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new CosmosDbEventStoreOptions();
        _cosmosClient = cosmosClient ?? throw new ArgumentNullException(nameof(cosmosClient));
        _databaseName = databaseName;
        _ownsCosmosClient = false;
    }

    /// <summary>
    ///     Gets the event store options.
    /// </summary>
    public CosmosDbEventStoreOptions Options => _options;

    /// <summary>
    ///     Gets the events container, initializing if needed.
    /// </summary>
    public async Task<Container> GetEventsContainerAsync()
    {
        if (_eventsContainer != null)
            return _eventsContainer;

        await InitializeAsync().ConfigureAwait(false);
        return _eventsContainer!;
    }

    /// <summary>
    ///     Gets the tags container, initializing if needed.
    /// </summary>
    public async Task<Container> GetTagsContainerAsync()
    {
        if (_tagsContainer != null)
            return _tagsContainer;

        await InitializeAsync().ConfigureAwait(false);
        return _tagsContainer!;
    }

    /// <summary>
    ///     Gets the multi projection states container, initializing if needed.
    /// </summary>
    public async Task<Container> GetMultiProjectionStatesContainerAsync()
    {
        if (_multiProjectionStatesContainer != null)
            return _multiProjectionStatesContainer;

        await InitializeAsync().ConfigureAwait(false);
        return _multiProjectionStatesContainer!;
    }

    private async Task InitializeAsync()
    {
        if (_database != null)
            return;

        if (_logger != null)
        {
            LogInitializingConnection(_logger, null);
        }

        if (_cosmosClient == null)
        {
            if (string.IsNullOrEmpty(_connectionString))
                throw new InvalidOperationException("No CosmosClient or connection string provided");

            var cosmosClientOptions = new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                },
                AllowBulkExecution = true,
                // Retry settings for Serverless mode (increased from defaults)
                MaxRetryAttemptsOnRateLimitedRequests = _options.MaxRetryAttemptsOnRateLimited,
                MaxRetryWaitTimeOnRateLimitedRequests = _options.MaxRetryWaitTime,
                // Use Direct mode for better read performance (TCP instead of HTTPS)
                ConnectionMode = _options.UseDirectConnectionMode ? ConnectionMode.Direct : ConnectionMode.Gateway
            };

            _cosmosClient = new CosmosClient(_connectionString, cosmosClientOptions);
        }

        // Create database if it doesn't exist
        var databaseResponse = await _cosmosClient.CreateDatabaseIfNotExistsAsync(_databaseName).ConfigureAwait(false);
        _database = databaseResponse.Database;

        if (_logger != null)
        {
            LogUsingDatabase(_logger, _databaseName, null);
        }

        // Create events container with partition key on id
        var eventsContainerProperties = new ContainerProperties
        {
            Id = "events",
            PartitionKeyPath = "/id"
        };

        var eventsContainerResponse = await _database.CreateContainerIfNotExistsAsync(
            eventsContainerProperties).ConfigureAwait(false);
        _eventsContainer = eventsContainerResponse.Container;

        if (_logger != null)
        {
            LogEventsContainerInitialized(_logger, null);
        }

        // Create tags container with partition key on tag
        var tagsContainerProperties = new ContainerProperties
        {
            Id = "tags",
            PartitionKeyPath = "/tag"
        };

        var tagsContainerResponse = await _database.CreateContainerIfNotExistsAsync(
            tagsContainerProperties).ConfigureAwait(false);
        _tagsContainer = tagsContainerResponse.Container;

        if (_logger != null)
        {
            LogTagsContainerInitialized(_logger, null);
        }

        // Create multi projection states container with partition key on partitionKey
        var statesContainerProperties = new ContainerProperties
        {
            Id = "multiProjectionStates",
            PartitionKeyPath = "/partitionKey"
        };

        var statesContainerResponse = await _database.CreateContainerIfNotExistsAsync(
            statesContainerProperties).ConfigureAwait(false);
        _multiProjectionStatesContainer = statesContainerResponse.Container;
    }

    /// <summary>
    ///     Disposes owned CosmosDB resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Protected dispose pattern hook.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing && _ownsCosmosClient)
        {
            _cosmosClient?.Dispose();
        }

        _disposed = true;
    }
}
