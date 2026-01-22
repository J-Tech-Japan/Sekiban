namespace Sekiban.Dcb.DynamoDB;

/// <summary>
///     Configuration options for DynamoDB event store.
/// </summary>
public class DynamoDbEventStoreOptions
{
    /// <summary>
    ///     Configuration section name.
    /// </summary>
    public const string SectionName = "DynamoDb";

    /// <summary>
    ///     Table name for events.
    /// </summary>
    public string EventsTableName { get; set; } = "SekibanEvents";

    /// <summary>
    ///     Table name for tags.
    /// </summary>
    public string TagsTableName { get; set; } = "SekibanTags";

    /// <summary>
    ///     Table name for multi-projection states.
    /// </summary>
    public string ProjectionStatesTableName { get; set; } = "SekibanProjectionStates";

    /// <summary>
    ///     Service URL for DynamoDB (for LocalStack or DynamoDB Local).
    ///     Leave null to use AWS default endpoint.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    ///     Maximum concurrent read operations for parallel deserialization.
    /// </summary>
    public int MaxConcurrentReads { get; set; } = Environment.ProcessorCount * 2;

    /// <summary>
    ///     Maximum items per BatchGetItem request (DynamoDB limit is 100).
    /// </summary>
    public int MaxBatchGetItems { get; set; } = 100;

    /// <summary>
    ///     Maximum items per TransactWriteItems request (DynamoDB limit is 100).
    /// </summary>
    public int MaxTransactionItems { get; set; } = 100;

    /// <summary>
    ///     Maximum items per BatchWriteItem request (DynamoDB limit is 25).
    /// </summary>
    public int MaxBatchWriteItems { get; set; } = 25;

    /// <summary>
    ///     Use strongly consistent reads for table queries (not GSI).
    /// </summary>
    public bool UseConsistentReads { get; set; }

    /// <summary>
    ///     Maximum retry attempts for throttled requests.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 10;

    /// <summary>
    ///     Maximum delay for retry backoff.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Number of shards for GSI partition key to prevent hot partitions.
    ///     Default: 1 (no sharding). Set higher for > 1000 writes/second.
    /// </summary>
    public int WriteShardCount { get; set; } = 1;

    /// <summary>
    ///     Automatically create tables if they don't exist.
    /// </summary>
    public bool AutoCreateTables { get; set; } = true;

    /// <summary>
    ///     Threshold in bytes for offloading projection state to blob storage.
    ///     DynamoDB item limit is 400KB, so default is 350KB to leave margin.
    /// </summary>
    public long OffloadThresholdBytes { get; set; } = 350_000;

    /// <summary>
    ///     Try to rollback events if tag writes fail.
    /// </summary>
    public bool TryRollbackOnFailure { get; set; } = true;

    /// <summary>
    ///     Page size for query operations.
    /// </summary>
    public int QueryPageSize { get; set; } = 1000;

    /// <summary>
    ///     Callback for reporting read progress (events read, consumed capacity).
    /// </summary>
    public Action<long, double>? ReadProgressCallback { get; set; }
}
