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
}
