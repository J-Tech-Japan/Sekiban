using Sekiban.Dcb.Snapshots;
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
    ///     If the serialized snapshot size exceeds this value (bytes), offload payload to <see cref="SnapshotAccessor"/>.
    ///     Set to 0 or negative to disable offloading. Default is 2MB.
    /// </summary>
    public int SnapshotOffloadThresholdBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>
    ///     Snapshot offload storage accessor. When null, offloading is disabled regardless of threshold.
    /// </summary>
    public IBlobStorageSnapshotAccessor? SnapshotAccessor { get; set; }

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
}
