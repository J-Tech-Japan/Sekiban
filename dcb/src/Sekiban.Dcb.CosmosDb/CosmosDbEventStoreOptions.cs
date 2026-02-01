using Microsoft.Azure.Cosmos;
namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Configuration options for CosmosDB event store operations.
/// </summary>
public class CosmosDbEventStoreOptions
{
    /// <summary>
    ///     Maximum number of concurrent event write operations.
    ///     Events are written in parallel with this concurrency limit.
    ///     Default: 10 (conservative for Serverless mode)
    /// </summary>
    public int MaxConcurrentEventWrites { get; set; } = 10;

    /// <summary>
    ///     Whether to use TransactionalBatch for tag writes.
    ///     When true, tags with the same partition key are batched together.
    ///     Default: true
    /// </summary>
    public bool UseTransactionalBatchForTags { get; set; } = true;

    /// <summary>
    ///     Maximum operations per TransactionalBatch (Cosmos DB limit: 100).
    ///     Default: 100
    /// </summary>
    public int MaxBatchOperations { get; set; } = 100;

    /// <summary>
    ///     Whether to attempt rollback (delete written events) on failure.
    ///     Default: true
    /// </summary>
    public bool TryRollbackOnFailure { get; set; } = true;

    /// <summary>
    ///     Maximum retry attempts for rate-limited requests.
    ///     Default: 15 (increased for Serverless mode)
    /// </summary>
    public int MaxRetryAttemptsOnRateLimited { get; set; } = 15;

    /// <summary>
    ///     Maximum wait time for rate-limited retries.
    ///     Default: 60 seconds (increased for Serverless mode)
    /// </summary>
    public TimeSpan MaxRetryWaitTime { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     Whether to enable content response on write operations.
    ///     Setting to false reduces RU consumption.
    ///     Default: false (disabled for better performance)
    /// </summary>
    public bool EnableContentResponseOnWrite { get; set; }

    /// <summary>
    ///     Container name for events (v2).
    /// </summary>
    public string EventsContainerName { get; set; } = "events";

    /// <summary>
    ///     Container name for tags (v2).
    /// </summary>
    public string TagsContainerName { get; set; } = "tags";

    /// <summary>
    ///     Container name for multi projection states (v2).
    /// </summary>
    public string MultiProjectionStatesContainerName { get; set; } = "multiProjectionStates";

    /// <summary>
    ///     Legacy container name for events.
    /// </summary>
    public string LegacyEventsContainerName { get; set; } = "events";

    /// <summary>
    ///     Legacy container name for tags.
    /// </summary>
    public string LegacyTagsContainerName { get; set; } = "tags";

    /// <summary>
    ///     Legacy container name for multi projection states.
    /// </summary>
    public string LegacyMultiProjectionStatesContainerName { get; set; } = "multiProjectionStates";

    /// <summary>
    ///     Whether to use legacy partition key paths (/id, /tag, /partitionKey) for default tenant.
    ///     Default: false (uses /pk).
    /// </summary>
    public bool UseLegacyPartitionKeyPaths { get; set; }

    // ========== Read Optimization Options ==========

    /// <summary>
    ///     Maximum items per page when reading events from Cosmos DB.
    ///     Higher values reduce round trips but increase memory usage.
    ///     Default: 1000 (optimized for Azure Container Apps / Orleans)
    /// </summary>
    public int MaxItemCountPerPage { get; set; } = 1000;

    /// <summary>
    ///     Maximum degree of parallelism for cross-partition queries.
    ///     Default: -1 (unlimited, let Cosmos DB SDK decide)
    /// </summary>
    public int MaxConcurrencyForQueries { get; set; } = -1;

    /// <summary>
    ///     Maximum buffered items for cross-partition queries.
    ///     Default: 50000 (optimized for high-throughput reads)
    /// </summary>
    public int MaxBufferedItemCount { get; set; } = 50000;

    /// <summary>
    ///     Whether to use Direct connection mode (TCP) instead of Gateway (HTTPS).
    ///     Direct mode offers significantly better performance.
    ///     Default: true (optimized for Azure Container Apps / Orleans)
    ///     Note: Set to false if running behind proxies/firewalls or in Azure Functions Consumption plan.
    /// </summary>
    public bool UseDirectConnectionMode { get; set; } = true;

    /// <summary>
    ///     Maximum concurrent deserialization tasks when processing read results.
    ///     Default: Environment.ProcessorCount * 2 (optimized for parallel processing)
    /// </summary>
    public int MaxConcurrentDeserializations { get; set; } = Environment.ProcessorCount * 2;

    /// <summary>
    ///     Callback for progress reporting during bulk read operations.
    ///     Called with (eventsRead, totalRuConsumed) after each page.
    /// </summary>
    public Action<int, double>? ReadProgressCallback { get; set; }

    /// <summary>
    ///     Creates QueryRequestOptions configured based on current settings.
    ///     Values of -1 are omitted to use SDK defaults.
    /// </summary>
    public QueryRequestOptions CreateOptimizedQueryRequestOptions()
    {
        var options = new QueryRequestOptions();

        if (MaxItemCountPerPage > 0)
            options.MaxItemCount = MaxItemCountPerPage;

        if (MaxConcurrencyForQueries != -1)
            options.MaxConcurrency = MaxConcurrencyForQueries;

        if (MaxBufferedItemCount > 0)
            options.MaxBufferedItemCount = MaxBufferedItemCount;

        return options;
    }

    /// <summary>
    ///     Creates a compatibility-focused options instance for restricted environments.
    ///     Use this for local testing behind proxies, Azure Functions Consumption plan,
    ///     or other environments where Direct mode may not work.
    /// </summary>
    public static CosmosDbEventStoreOptions CreateForCompatibility() =>
        new()
        {
            MaxItemCountPerPage = -1, // Use SDK default (~100)
            MaxConcurrencyForQueries = -1,
            MaxBufferedItemCount = -1, // Use SDK default
            UseDirectConnectionMode = false, // Gateway mode (HTTPS)
            MaxConcurrentDeserializations = 1 // Sequential processing
        };
}
