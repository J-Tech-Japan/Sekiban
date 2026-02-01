using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
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
    private readonly Dictionary<string, Container> _containers = new();
    private CosmosClient? _cosmosClient;
    private Database? _database;
    private bool _disposed;
    private readonly bool _ownsCosmosClient;
    private readonly SemaphoreSlim _containerLock = new(1, 1);

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
    ///     Gets the events container for the provided settings, initializing if needed.
    /// </summary>
    public Task<Container> GetEventsContainerAsync(CosmosContainerSettings settings) =>
        GetOrCreateContainerAsync(settings, CreateEventsContainerProperties);

    /// <summary>
    ///     Gets the tags container for the provided settings, initializing if needed.
    /// </summary>
    public Task<Container> GetTagsContainerAsync(CosmosContainerSettings settings) =>
        GetOrCreateContainerAsync(settings, CreateTagsContainerProperties);

    /// <summary>
    ///     Gets the multi projection states container for the provided settings, initializing if needed.
    /// </summary>
    public Task<Container> GetMultiProjectionStatesContainerAsync(CosmosContainerSettings settings) =>
        GetOrCreateContainerAsync(settings, CreateStatesContainerProperties);

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

        // Containers are created lazily per settings.
    }

    private async Task<Container> GetOrCreateContainerAsync(
        CosmosContainerSettings settings,
        Func<CosmosContainerSettings, ContainerProperties> propertiesFactory)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (_containers.TryGetValue(settings.Name, out var cached))
        {
            return cached;
        }

        await InitializeAsync().ConfigureAwait(false);

        await _containerLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_containers.TryGetValue(settings.Name, out cached))
            {
                return cached;
            }

            var properties = propertiesFactory(settings);
            var response = await _database!.CreateContainerIfNotExistsAsync(properties).ConfigureAwait(false);
            var container = response.Container;

            _containers[settings.Name] = container;

            if (_logger != null)
            {
                if (string.Equals(settings.Name, _options.EventsContainerName, StringComparison.Ordinal) ||
                    string.Equals(settings.Name, _options.LegacyEventsContainerName, StringComparison.Ordinal))
                {
                    LogEventsContainerInitialized(_logger, null);
                }
                else if (string.Equals(settings.Name, _options.TagsContainerName, StringComparison.Ordinal) ||
                         string.Equals(settings.Name, _options.LegacyTagsContainerName, StringComparison.Ordinal))
                {
                    LogTagsContainerInitialized(_logger, null);
                }
            }

            return container;
        }
        finally
        {
            _containerLock.Release();
        }
    }

    private ContainerProperties CreateEventsContainerProperties(CosmosContainerSettings settings)
    {
        var properties = new ContainerProperties
        {
            Id = settings.Name,
            PartitionKeyPath = settings.PartitionKeyPath
        };

        if (!settings.IsLegacy)
        {
            properties.IndexingPolicy.CompositeIndexes.Add(new Collection<CompositePath>
            {
                new() { Path = "/serviceId", Order = CompositePathSortOrder.Ascending },
                new() { Path = "/sortableUniqueId", Order = CompositePathSortOrder.Ascending }
            });
        }

        return properties;
    }

    private ContainerProperties CreateTagsContainerProperties(CosmosContainerSettings settings)
    {
        var properties = new ContainerProperties
        {
            Id = settings.Name,
            PartitionKeyPath = settings.PartitionKeyPath
        };

        if (!settings.IsLegacy)
        {
            properties.IndexingPolicy.CompositeIndexes.Add(new Collection<CompositePath>
            {
                new() { Path = "/serviceId", Order = CompositePathSortOrder.Ascending },
                new() { Path = "/tag", Order = CompositePathSortOrder.Ascending },
                new() { Path = "/sortableUniqueId", Order = CompositePathSortOrder.Ascending }
            });
        }

        return properties;
    }

    private static ContainerProperties CreateStatesContainerProperties(CosmosContainerSettings settings) =>
        new()
        {
            Id = settings.Name,
            PartitionKeyPath = settings.PartitionKeyPath
        };

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

        if (disposing)
        {
            _containerLock.Dispose();
        }

        _disposed = true;
    }
}
