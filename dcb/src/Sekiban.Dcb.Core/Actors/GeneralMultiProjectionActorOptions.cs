namespace Sekiban.Dcb.Actors;

/// <summary>
///     Configuration options for GeneralMultiProjection actors
/// </summary>
public class GeneralMultiProjectionActorOptions
{
    /// <summary>
    ///     The safe window time in milliseconds to wait before processing events
    ///     to ensure consistency. Events within this window may arrive out of order.
    ///     Default is 20000 milliseconds (20 seconds).
    /// </summary>
    public int SafeWindowMs { get; set; } = 20000;

    /// <summary>
    ///     Maximum allowed size (bytes) of the serialized snapshot (Envelope JSON).
    ///     If positive, BuildSnapshotForPersistenceAsync will fail when the size exceeds this value.
    ///     Default is 2MB to align with Cosmos DB item size limits.
    /// </summary>
    public int MaxSnapshotSerializedSizeBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>
    ///     Maximum number of stream events to buffer while catch-up is active.
    ///     Set to 0 or negative to disable trimming (use with care).
    /// </summary>
    public int MaxPendingStreamEvents { get; set; } = 50000;

    /// <summary>
    ///     Number of events read per catch-up batch from the event store.
    ///     Lower values reduce peak memory usage during catch-up.
    /// </summary>
    public int CatchUpBatchSize { get; set; } = 500;

    /// <summary>
    ///     Delay deactivation window while catch-up is active.
    ///     The delay is renewed for each catch-up batch.
    /// </summary>
    public int CatchUpDeactivationDelayMinutes { get; set; } = 10;

    /// <summary>
    ///     Maximum consecutive catch-up batch failures before stopping catch-up.
    /// </summary>
    public int CatchUpMaxConsecutiveFailures { get; set; } = 120;

    /// <summary>
    ///     Maximum failure window in seconds while catch-up keeps failing.
    ///     Once exceeded, catch-up is stopped to avoid permanent active loops.
    /// </summary>
    public int CatchUpMaxFailureDurationSeconds { get; set; } = 300;

    /// <summary>
    ///     Number of events in a live stream batch that triggers snapshot persistence.
    ///     Set to 0 or negative to disable batch-triggered persistence.
    /// </summary>
    public int PersistBatchSize { get; set; } = 10_000;

    /// <summary>
    ///     Periodic persistence interval in seconds.
    ///     Set to 0 or negative to disable the periodic persistence timer.
    /// </summary>
    public int PersistIntervalSeconds { get; set; } = 60 * 60;

    /// <summary>
    ///     When true (default), skips persistence when the safe checkpoint has not
    ///     advanced since the last successful persist for the same projector version.
    /// </summary>
    public bool SkipPersistWhenSafeCheckpointUnchanged { get; set; } = true;

    /// <summary>
    ///     Optional projector-specific overrides for persistence policy.
    ///     Keys are projector names (e.g. "KanyushaListProjection").
    /// </summary>
    public Dictionary<string, MultiProjectionPersistenceOverrideOptions> ProjectorPersistenceOverrides { get; set; } =
        new(StringComparer.Ordinal);

    // Dynamic SafeWindow controls (optional; default OFF)
    public bool EnableDynamicSafeWindow { get; set; } = false;

    /// <summary>
    /// Maximum extra milliseconds that can be added to SafeWindow by dynamic lag tracking.
    /// </summary>
    public int MaxExtraSafeWindowMs { get; set; } = 30000;

    /// <summary>
    /// Exponential moving average alpha for stream lag (0..1]. Larger values react faster.
    /// </summary>
    public double LagEmaAlpha { get; set; } = 0.2;

    /// <summary>
    /// Per-second decay factor applied to observed lag when no updates (0..1]. Closer to 1 decays slower.
    /// </summary>
    public double LagDecayPerSecond { get; set; } = 0.98;

    /// <summary>
    ///     When true, queries return explicit errors if state restoration failed during activation.
    ///     When false (default), queries return empty results for backward compatibility.
    ///     Enable this for stricter error handling in production environments.
    /// </summary>
    public bool FailOnUnhealthyActivation { get; set; } = false;

    /// <summary>
    ///     Maximum number of processed event IDs to keep for duplicate suppression.
    ///     Lower values reduce memory usage; too low may allow rare duplicate re-processing.
    /// </summary>
    public int ProcessedEventIdCacheSize { get; set; } = 200000;

    /// <summary>
    ///     Whether to force a Gen2 GC with LOH compaction after persisting a large snapshot.
    /// </summary>
    public bool ForceGcAfterLargeSnapshotPersist { get; set; } = true;

    /// <summary>
    ///     Snapshot size threshold (bytes) that triggers the optional post-persist GC.
    /// </summary>
    public long LargeSnapshotGcThresholdBytes { get; set; } = 10_000_000;

    /// <summary>
    ///     When true (default), uses the stream-based snapshot I/O path for persistence.
    ///     When false, uses the existing byte[] path for backward compatibility.
    /// </summary>
    public bool UseStreamingSnapshotIO { get; set; } = true;
}

public sealed class MultiProjectionPersistenceOverrideOptions
{
    /// <summary>
    ///     Overrides GeneralMultiProjectionActorOptions.PersistBatchSize for a specific projector.
    ///     Set to 0 or negative to disable batch-triggered persistence for that projector.
    /// </summary>
    public int? PersistBatchSize { get; set; }

    /// <summary>
    ///     Overrides GeneralMultiProjectionActorOptions.PersistIntervalSeconds for a specific projector.
    ///     Set to 0 or negative to disable periodic persistence for that projector.
    /// </summary>
    public int? PersistIntervalSeconds { get; set; }

    /// <summary>
    ///     Overrides GeneralMultiProjectionActorOptions.SkipPersistWhenSafeCheckpointUnchanged
    ///     for a specific projector.
    /// </summary>
    public bool? SkipPersistWhenSafeCheckpointUnchanged { get; set; }
}
