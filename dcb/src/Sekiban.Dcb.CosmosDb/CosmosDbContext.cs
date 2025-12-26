using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Context for managing CosmosDB containers
/// </summary>
public class CosmosDbContext : IDisposable
{
    private readonly string? _connectionString;
    private readonly string _databaseName;
    private readonly ILogger<CosmosDbContext>? _logger;
    private Container? _eventsContainer;
    private Container? _tagsContainer;
    private CosmosClient? _cosmosClient;
    private Database? _database;
    private bool _disposed;
    private readonly bool _ownsCosmosClient;

    /// <summary>
    ///     Constructor from configuration (deprecated - use extension methods instead)
    /// </summary>
    [Obsolete("Use SekibanDcbCosmosDbExtensions.AddSekibanDcbCosmosDb instead")]
    public CosmosDbContext(IConfiguration configuration, ILogger<CosmosDbContext>? logger = null)
    {
        _logger = logger;
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

    public CosmosDbContext(string connectionString, string databaseName = "SekibanDcb", ILogger<CosmosDbContext>? logger = null)
    {
        _logger = logger;
        _connectionString = connectionString;
        _databaseName = databaseName;
        _ownsCosmosClient = true;
    }

    /// <summary>
    ///     Constructor that accepts an existing CosmosClient (for Aspire)
    /// </summary>
    public CosmosDbContext(CosmosClient cosmosClient, string databaseName = "SekibanDcb", ILogger<CosmosDbContext>? logger = null)
    {
        _logger = logger;
        _cosmosClient = cosmosClient ?? throw new ArgumentNullException(nameof(cosmosClient));
        _databaseName = databaseName;
        _ownsCosmosClient = false;
    }

    public async Task<Container> GetEventsContainerAsync()
    {
        if (_eventsContainer != null)
            return _eventsContainer;

        await InitializeAsync().ConfigureAwait(false);
        return _eventsContainer!;
    }

    public async Task<Container> GetTagsContainerAsync()
    {
        if (_tagsContainer != null)
            return _tagsContainer;

        await InitializeAsync().ConfigureAwait(false);
        return _tagsContainer!;
    }

    private async Task InitializeAsync()
    {
        if (_database != null)
            return;

        _logger?.LogInformation("Initializing CosmosDB connection");

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
                AllowBulkExecution = true
            };

            _cosmosClient = new CosmosClient(_connectionString, cosmosClientOptions);
        }

        // Create database if it doesn't exist
        var databaseResponse = await _cosmosClient.CreateDatabaseIfNotExistsAsync(_databaseName).ConfigureAwait(false);
        _database = databaseResponse.Database;

        _logger?.LogInformation($"Using CosmosDB database: {_databaseName}");

        // Create events container with partition key on id
        var eventsContainerProperties = new ContainerProperties
        {
            Id = "events",
            PartitionKeyPath = "/id"
        };

        var eventsContainerResponse = await _database.CreateContainerIfNotExistsAsync(
            eventsContainerProperties).ConfigureAwait(false);
        _eventsContainer = eventsContainerResponse.Container;

        _logger?.LogInformation("Events container initialized");

        // Create tags container with partition key on tag
        var tagsContainerProperties = new ContainerProperties
        {
            Id = "tags",
            PartitionKeyPath = "/tag"
        };

        var tagsContainerResponse = await _database.CreateContainerIfNotExistsAsync(
            tagsContainerProperties).ConfigureAwait(false);
        _tagsContainer = tagsContainerResponse.Container;

        _logger?.LogInformation("Tags container initialized");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_ownsCosmosClient)
        {
            _cosmosClient?.Dispose();
        }
        _disposed = true;
    }
}
