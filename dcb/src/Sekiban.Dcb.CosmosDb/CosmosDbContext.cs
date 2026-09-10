using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
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
    // ConcurrentDictionary: the fast-path read (below) is lock-free, so a plain Dictionary being written under
    // _containerLock would be a torn-read/resize hazard across async thread hops. SEK-G20's checkpoint CAS depends on a
    // consistent container resolution across sequential awaited calls.
    private readonly ConcurrentDictionary<string, Container> _containers = new();
    private CosmosClient? _cosmosClient;
    private Database? _database;
    private bool _disposed;
    private int _clientCreationCount;
    private readonly bool _ownsCosmosClient;
    private readonly object _lifecycleLock = new();
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
    ///     Test-only instrumentation for the provider-owned lazy client path. It is internal so production consumers
    ///     cannot depend on it; it does not create a client or initialize a database.
    /// </summary>
    internal int ClientCreationCount
    {
        get
        {
            lock (_lifecycleLock)
            {
                return _clientCreationCount;
            }
        }
    }

    /// <summary>
    ///     Instance-local test control for the provider-owned serializer capability. It simulates an SDK client whose
    ///     effective serializer is unavailable without allowing tests or callers to install an arbitrary serializer.
    /// </summary>
    internal bool ForceMeasurementSerializerUnavailable { get; set; }

    /// <summary>
    ///     Measures a mapped event document with the exact provider-owned SDK serializer used by this context.
    ///     Injected clients are deliberately not certified because their serializer is not observable here.
    /// </summary>
    internal bool TryMeasureSupportedDocument(object document, out long bytes, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(document);

        lock (_lifecycleLock)
        {
            bytes = 0;
            reason = null;

            if (_disposed)
            {
                reason = "CosmosDbContext is disposed";
                return false;
            }

            if (!_ownsCosmosClient)
            {
                reason = "Cosmos provider serializer capability is unavailable for an injected CosmosClient; injected clients are unproven";
                return false;
            }

            try
            {
                EnsureClientCreatedLocked();
                var serializer = ForceMeasurementSerializerUnavailable
                    ? null
                    : _cosmosClient!.ClientOptions.Serializer;
                if (serializer is null)
                {
                    reason = "Cosmos provider serializer capability is unavailable";
                    return false;
                }

                using var stream = serializer.ToStream(document);
                if (!stream.CanSeek)
                {
                    reason = "provider serializer returned a non-seekable stream";
                    return false;
                }

                bytes = stream.Length;
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                reason = $"provider serializer failed with {ex.GetType().Name}";
                return false;
            }
        }
    }

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
        lock (_lifecycleLock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CosmosDbContext));

            if (_database != null)
                return;
        }

        if (_logger != null)
        {
            LogInitializingConnection(_logger, null);
        }

        var client = EnsureClientCreated();

        // Create database if it doesn't exist
        var databaseResponse = await client.CreateDatabaseIfNotExistsAsync(_databaseName).ConfigureAwait(false);
        lock (_lifecycleLock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CosmosDbContext));

            _database = databaseResponse.Database;
        }

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
        Container? cached;

        lock (_lifecycleLock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CosmosDbContext));

            if (_containers.TryGetValue(settings.Name, out cached))
                return cached;
        }

        await InitializeAsync().ConfigureAwait(false);

        await _containerLock.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_lifecycleLock)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(CosmosDbContext));

                if (_containers.TryGetValue(settings.Name, out cached))
                    return cached;
            }

            var properties = propertiesFactory(settings);
            var response = await _database!.CreateContainerIfNotExistsAsync(properties).ConfigureAwait(false);
            var container = response.Container;

            _containers[settings.Name] = container;

            if (_logger != null)
            {
                if (string.Equals(settings.Name, _options.EventsContainerName, StringComparison.Ordinal))
                {
                    LogEventsContainerInitialized(_logger, null);
                }
                else if (string.Equals(settings.Name, _options.TagsContainerName, StringComparison.Ordinal))
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

        properties.IndexingPolicy.CompositeIndexes.Add(new Collection<CompositePath>
        {
            new() { Path = "/serviceId", Order = CompositePathSortOrder.Ascending },
            new() { Path = "/sortableUniqueId", Order = CompositePathSortOrder.Ascending }
        });

        return properties;
    }

    private ContainerProperties CreateTagsContainerProperties(CosmosContainerSettings settings)
    {
        var properties = new ContainerProperties
        {
            Id = settings.Name,
            PartitionKeyPath = settings.PartitionKeyPath
        };

        properties.IndexingPolicy.CompositeIndexes.Add(new Collection<CompositePath>
        {
            new() { Path = "/serviceId", Order = CompositePathSortOrder.Ascending },
            new() { Path = "/tag", Order = CompositePathSortOrder.Ascending },
            new() { Path = "/sortableUniqueId", Order = CompositePathSortOrder.Ascending }
        });

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
    ///     Protected dispose pattern hook. Disposing the context does not make general in-flight Cosmos database I/O
    ///     crash-safe; callers must coordinate shutdown with their outstanding operations.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
                return;

            _disposed = true;

            if (disposing && _ownsCosmosClient)
                _cosmosClient?.Dispose();

            if (disposing)
                _containerLock.Dispose();
        }
    }

    private CosmosClient EnsureClientCreated()
    {
        lock (_lifecycleLock)
        {
            EnsureClientCreatedLocked();
            return _cosmosClient!;
        }
    }

    private void EnsureClientCreatedLocked()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CosmosDbContext));

        if (_cosmosClient is not null)
            return;

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
        _clientCreationCount++;
    }
}
