using Microsoft.Azure.Cosmos;
namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Configuration options for CosmosDB event store operations.
/// </summary>
public class CosmosDbEventStoreOptions
{
    /// <summary>
    ///     Default upper bound for keeping multi projection snapshots inline in Cosmos DB.
    /// </summary>
    public const int DefaultMultiProjectionStateOffloadThresholdBytes = 1_000_000;

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
    ///     WARNING: rollback DELETES durable event documents that all-events consumers — multi-projections
    ///     above all — may already have read, which contaminates their state irreversibly. It also only runs
    ///     on an in-process exception, so it never runs after a crash and cannot be relied on for atomicity.
    ///     Set <see cref="WriteFailurePolicy" /> to <see cref="CosmosWriteFailurePolicy.RollForward" />
    ///     instead: the tag write is retried, no event is ever deleted, and an unrecoverable failure names
    ///     the events whose tag rows may be missing so they can be repaired.
    ///     Kept as-is (and still honored under the compatible default) so that upgrading the package alone
    ///     does not change the behavior of an existing deployment.
    /// </summary>
    [Obsolete(
        "Rollback deletes durable events that all-events consumers may already have observed, and never runs after a crash. " +
        "Set WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward instead. " +
        "This option is honored under the compatible default and will be removed at a major version boundary.")]
    public bool TryRollbackOnFailure { get; set; } = true;

    /// <summary>
    ///     What happens when the tag-write phase fails.
    ///     Default: <see cref="CosmosWriteFailurePolicy.Compatible" /> — the behavior of earlier releases, so
    ///     that a package upgrade alone changes nothing. Opt into
    ///     <see cref="CosmosWriteFailurePolicy.RollForward" /> to retry tag writes instead of deleting events.
    ///     The default flips to roll-forward only at a major version boundary, with a documented migration.
    /// </summary>
    public CosmosWriteFailurePolicy WriteFailurePolicy { get; set; } = CosmosWriteFailurePolicy.Compatible;

    /// <summary>
    ///     Retry policy for the tag-write phase. Only consulted under
    ///     <see cref="CosmosWriteFailurePolicy.RollForward" />.
    /// </summary>
    public CosmosTagWriteRetryOptions TagWriteRetry { get; set; } = new();

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
    ///     Storage-specific upper bound for keeping multi projection snapshots inline in Cosmos DB.
    ///     Cosmos state documents carry the snapshot payload as Base64 text, so using the actor's 2MB
    ///     snapshot size limit as-is can still exceed the final item size. Default is 1,000,000 bytes.
    /// </summary>
    public int MultiProjectionStateOffloadThresholdBytes { get; set; } = DefaultMultiProjectionStateOffloadThresholdBytes;

    // ========== Read Optimization Options ==========

    /// <summary>
    ///     Maximum items per page when reading events from Cosmos DB.
    ///     Higher values reduce round trips but increase memory usage.
    ///     Default: 1000 (optimized for Azure Container Apps / Orleans)
    /// </summary>
    public int MaxItemCountPerPage { get; set; } = 1000;

    /// <summary>
    ///     Maximum items per page specifically for ReadAllEventsAsync.
    ///     This is separate from maxCount (total events requested by caller).
    ///     Default: 500 to keep per-page memory usage low in constrained environments.
    /// </summary>
    public int MaxItemCountPerReadAllPage { get; set; } = 500;

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
    ///     Default maximum number of in-flight event point reads used only by the native tagged-stream path.
    ///     It is intentionally separate from <see cref="MaxConcurrentDeserializations" />: a tagged stream is an
    ///     ordered, bounded producer rather than a bulk list deserialization operation.
    /// </summary>
    public const int DefaultMaxConcurrentTaggedStreamPointReads = 8;

    /// <summary>Hard upper bound for <see cref="MaxConcurrentTaggedStreamPointReads" />.</summary>
    public const int MaximumMaxConcurrentTaggedStreamPointReads = 64;

    // The compatibility preset deliberately retains MaxItemCountPerPage = -1 for its legacy readers. Native tagged
    // streams instead make that SDK-default sentinel explicit at their own boundary so W remains coupled to a known
    // page size without changing any existing list-reader request shape.
    internal const int DefaultTaggedStreamIndexPageSize = 100;

    /// <summary>
    ///     Maximum event point reads the native tagged stream may issue before it has emitted the queue head.
    ///     The value is validated for every tagged stream: 1 through 64 and no larger than its tag-index page size.
    /// </summary>
    public int MaxConcurrentTaggedStreamPointReads { get; set; } = DefaultMaxConcurrentTaggedStreamPointReads;

    /// <summary>
    ///     Optional bounded telemetry seam for one native tagged-stream invocation. The callback receives aggregate
    ///     page/read/RU/throttle values only; it never receives event identifiers or tag strings.
    /// </summary>
    public Action<CosmosTaggedStreamTelemetry>? TaggedStreamTelemetryCallback { get; set; }

    /// <summary>
    ///     Callback for progress reporting during bulk read operations.
    ///     Called with (eventsRead, totalRuConsumed) after each page.
    /// </summary>
    public Action<int, double>? ReadProgressCallback { get; set; }

    /// <summary>
    ///     Validates and returns the native tagged-stream point-read window. A tagged stream needs a concrete page cap
    ///     to make the one-page-plus-window memory bound meaningful, so the SDK-default sentinel is rejected here.
    /// </summary>
    public int ValidateTaggedStreamPointReadWindow()
    {
        var pageSize = GetTaggedStreamIndexPageSize();

        if (MaxConcurrentTaggedStreamPointReads is < 1 or > MaximumMaxConcurrentTaggedStreamPointReads)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxConcurrentTaggedStreamPointReads),
                MaxConcurrentTaggedStreamPointReads,
                $"Native tagged-stream point reads must be between 1 and {MaximumMaxConcurrentTaggedStreamPointReads}.");
        }

        if (MaxConcurrentTaggedStreamPointReads > pageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxConcurrentTaggedStreamPointReads),
                MaxConcurrentTaggedStreamPointReads,
                "Native tagged-stream point reads may not exceed MaxItemCountPerPage.");
        }

        return MaxConcurrentTaggedStreamPointReads;
    }

    /// <summary>
    ///     Returns the page cap used only by a native tagged stream. The old SDK-default sentinel remains untouched for
    ///     compatible list readers, while this path uses an explicit cap to retain the <c>W &lt;= page size</c> invariant.
    /// </summary>
    internal int GetTaggedStreamIndexPageSize()
    {
        if (MaxItemCountPerPage == -1)
        {
            return DefaultTaggedStreamIndexPageSize;
        }

        if (MaxItemCountPerPage < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxItemCountPerPage),
                MaxItemCountPerPage,
                "Native tagged streaming requires MaxItemCountPerPage to be at least one or the SDK-default sentinel (-1).");
        }

        return MaxItemCountPerPage;
    }

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
    ///     Resolves the effective inline snapshot threshold for multi projection state persistence.
    ///     The caller-provided threshold is treated as an upper bound, and Cosmos may lower it to keep
    ///     the final document safely below item size limits.
    /// </summary>
    public int GetEffectiveMultiProjectionStateOffloadThresholdBytes(int requestedThresholdBytes)
    {
        var effectiveThreshold = requestedThresholdBytes > 0
            ? requestedThresholdBytes
            : DefaultMultiProjectionStateOffloadThresholdBytes;

        if (MultiProjectionStateOffloadThresholdBytes > 0 &&
            MultiProjectionStateOffloadThresholdBytes < effectiveThreshold)
        {
            effectiveThreshold = MultiProjectionStateOffloadThresholdBytes;
        }

        return effectiveThreshold;
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
            MaxItemCountPerReadAllPage = 100, // Keep pages small in compatibility mode
            MaxConcurrencyForQueries = -1,
            MaxBufferedItemCount = -1, // Use SDK default
            UseDirectConnectionMode = false, // Gateway mode (HTTPS)
            MaxConcurrentDeserializations = 1 // Sequential processing
        };
}

/// <summary>
///     Aggregate telemetry for one Cosmos native tagged stream. It deliberately contains no high-cardinality identifiers.
/// </summary>
public sealed record CosmosTaggedStreamTelemetry(
    int IndexPages,
    int PointReads,
    int PeakInFlightPointReads,
    double RequestCharge,
    int ThrottledRequests);
