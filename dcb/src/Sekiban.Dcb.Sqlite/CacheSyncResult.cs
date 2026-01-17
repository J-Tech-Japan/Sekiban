namespace Sekiban.Dcb.Sqlite;

/// <summary>
///     Result of a cache synchronization operation
/// </summary>
public record CacheSyncResult
{
    /// <summary>
    ///     Whether the sync operation succeeded
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    ///     Number of events synced in this operation
    /// </summary>
    public int EventsSynced { get; init; }

    /// <summary>
    ///     Total events in cache after sync
    /// </summary>
    public long TotalEventsInCache { get; init; }

    /// <summary>
    ///     Action taken during sync
    /// </summary>
    public CacheSyncAction Action { get; init; }

    /// <summary>
    ///     Error message if sync failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    ///     Duration of the sync operation
    /// </summary>
    public TimeSpan Duration { get; init; }

    public static CacheSyncResult Success(int eventsSynced, long totalEvents, CacheSyncAction action, TimeSpan duration) =>
        new()
        {
            IsSuccess = true,
            EventsSynced = eventsSynced,
            TotalEventsInCache = totalEvents,
            Action = action,
            Duration = duration
        };

    public static CacheSyncResult NoChanges(long totalEvents, TimeSpan duration) =>
        new()
        {
            IsSuccess = true,
            EventsSynced = 0,
            TotalEventsInCache = totalEvents,
            Action = CacheSyncAction.NoActionNeeded,
            Duration = duration
        };

    public static CacheSyncResult Failed(string error, TimeSpan duration) =>
        new()
        {
            IsSuccess = false,
            ErrorMessage = error,
            Action = CacheSyncAction.Failed,
            Duration = duration
        };
}

/// <summary>
///     Action taken during cache sync
/// </summary>
public enum CacheSyncAction
{
    /// <summary>
    ///     No sync was needed
    /// </summary>
    NoActionNeeded,

    /// <summary>
    ///     New events were appended to cache
    /// </summary>
    AppendedNewEvents,

    /// <summary>
    ///     Cache was rebuilt from scratch
    /// </summary>
    RebuiltFromScratch,

    /// <summary>
    ///     Cache was cleared
    /// </summary>
    CacheCleared,

    /// <summary>
    ///     Sync operation failed
    /// </summary>
    Failed
}
