using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.Sqlite;

/// <summary>
///     Synchronizes events from any IEventStore to a local SqliteEventStore.
///     Works with Cosmos DB, PostgreSQL, or any other IEventStore implementation.
///     Uses SerializableEvent path (no payload deserialization needed).
/// </summary>
public class EventStoreCacheSync
{
    private readonly SqliteEventStore _localStore;
    private readonly IEventStore _remoteStore;
    private readonly CacheSyncOptions _options;
    private readonly ILogger<EventStoreCacheSync>? _logger;

    public EventStoreCacheSync(
        SqliteEventStore localStore,
        IEventStore remoteStore,
        CacheSyncOptions? options = null,
        ILogger<EventStoreCacheSync>? logger = null)
    {
        _localStore = localStore;
        _remoteStore = remoteStore;
        _options = options ?? new CacheSyncOptions();
        _logger = logger;
    }

    /// <summary>
    ///     Synchronize the local cache with the remote event store.
    /// </summary>
    public async Task<CacheSyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger?.LogInformation("Starting cache sync...");

            // 1. Get cache metadata and validate
            var metadata = await _localStore.GetMetadataAsync();

            if (!ValidateMetadata(metadata))
            {
                _logger?.LogInformation("Cache metadata invalid or changed, clearing cache...");
                await _localStore.ClearAsync();
                metadata = null;
            }

            // 2. Get event counts
            var remoteCountResult = await _remoteStore.GetEventCountAsync();
            if (!remoteCountResult.IsSuccess)
            {
                return CacheSyncResult.Failed(
                    $"Failed to get remote event count: {remoteCountResult.GetException().Message}",
                    stopwatch.Elapsed);
            }

            var remoteCount = remoteCountResult.GetValue();

            var localCountResult = await _localStore.GetEventCountAsync();
            var localCount = localCountResult.IsSuccess ? localCountResult.GetValue() : 0;

            _logger?.LogInformation("Remote: {RemoteCount} events, Local: {LocalCount} events", remoteCount, localCount);

            // 3. Check if we need to rebuild (local > remote means deletion occurred)
            if (localCount > remoteCount)
            {
                _logger?.LogWarning("Local cache has more events than remote ({LocalCount} > {RemoteCount}), rebuilding...",
                    localCount, remoteCount);
                await _localStore.ClearAsync();
                return await RebuildFromScratchAsync(remoteCount, stopwatch, cancellationToken);
            }

            // 4. Check if up to date
            if (localCount == remoteCount)
            {
                _logger?.LogInformation("Cache is up to date");
                return CacheSyncResult.NoChanges(localCount, stopwatch.Elapsed);
            }

            // 5. Incremental sync
            var since = metadata?.LastCachedSortableUniqueId != null
                ? new SortableUniqueId(metadata.LastCachedSortableUniqueId)
                : null;
            var until = GetSafeWindowThreshold();

            _logger?.LogInformation("Fetching events since {Since} until {Until}",
                since?.Value ?? "(beginning)", until?.Value ?? "(now)");

            var remoteEventsResult = await _remoteStore.ReadAllSerializableEventsAsync(since);
            if (!remoteEventsResult.IsSuccess)
            {
                return CacheSyncResult.Failed(
                    $"Failed to read remote events: {remoteEventsResult.GetException().Message}",
                    stopwatch.Elapsed);
            }

            var remoteEvents = remoteEventsResult.GetValue().ToList();

            // Filter by safe window
            var eventsToCache = until != null
                ? remoteEvents.Where(e => new SortableUniqueId(e.SortableUniqueIdValue).IsEarlierThanOrEqual(until)).ToList()
                : remoteEvents;

            if (eventsToCache.Count == 0)
            {
                _logger?.LogInformation("No new events to cache (all within safe window)");
                return CacheSyncResult.NoChanges(localCount, stopwatch.Elapsed);
            }

            // 6. Write to local cache
            _logger?.LogInformation("Caching {Count} events...", eventsToCache.Count);

            var writeResult = await _localStore.WriteSerializableEventsAsync(eventsToCache);
            if (!writeResult.IsSuccess)
            {
                return CacheSyncResult.Failed(
                    $"Failed to write events to cache: {writeResult.GetException().Message}",
                    stopwatch.Elapsed);
            }

            // 7. Update metadata
            var newLocalCount = localCount + eventsToCache.Count;
            await _localStore.SetMetadataAsync(new CacheMetadata
            {
                RemoteEndpoint = _options.RemoteEndpoint,
                DatabaseName = _options.DatabaseName,
                SchemaVersion = _options.SchemaVersion,
                TotalCountAtFetch = remoteCount,
                LastCachedSortableUniqueId = eventsToCache.Last().SortableUniqueIdValue,
                LastSafeWindowUtc = until?.GetDateTime(),
                CreatedUtc = metadata?.CreatedUtc ?? DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });

            _logger?.LogInformation("Cache sync completed: {Count} events added, {Total} total",
                eventsToCache.Count, newLocalCount);

            return CacheSyncResult.Success(
                eventsToCache.Count,
                newLocalCount,
                CacheSyncAction.AppendedNewEvents,
                stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Cache sync failed");
            return CacheSyncResult.Failed(ex.Message, stopwatch.Elapsed);
        }
    }

    /// <summary>
    ///     Clear the local cache
    /// </summary>
    public async Task ClearAsync()
    {
        await _localStore.ClearAsync();
        _logger?.LogInformation("Cache cleared");
    }

    /// <summary>
    ///     Get cache statistics
    /// </summary>
    public async Task<CacheStatistics> GetStatisticsAsync()
    {
        var metadata = await _localStore.GetMetadataAsync();
        var eventCountResult = await _localStore.GetEventCountAsync();
        var eventCount = eventCountResult.IsSuccess ? eventCountResult.GetValue() : 0;

        var fileInfo = new FileInfo(_localStore.DatabasePath);
        var databaseSize = fileInfo.Exists ? fileInfo.Length : 0;

        return new CacheStatistics
        {
            EventCount = eventCount,
            DatabaseSizeBytes = databaseSize,
            RemoteEndpoint = metadata?.RemoteEndpoint,
            DatabaseName = metadata?.DatabaseName,
            LastCachedSortableUniqueId = metadata?.LastCachedSortableUniqueId,
            LastSyncUtc = metadata?.UpdatedUtc,
            CreatedUtc = metadata?.CreatedUtc
        };
    }

    private async Task<CacheSyncResult> RebuildFromScratchAsync(
        long remoteCount,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var until = GetSafeWindowThreshold();

        _logger?.LogInformation("Rebuilding cache from scratch...");

        var remoteEventsResult = await _remoteStore.ReadAllSerializableEventsAsync();
        if (!remoteEventsResult.IsSuccess)
        {
            return CacheSyncResult.Failed(
                $"Failed to read remote events: {remoteEventsResult.GetException().Message}",
                stopwatch.Elapsed);
        }

        var remoteEvents = remoteEventsResult.GetValue().ToList();

        // Filter by safe window
        var eventsToCache = until != null
            ? remoteEvents.Where(e => new SortableUniqueId(e.SortableUniqueIdValue).IsEarlierThanOrEqual(until)).ToList()
            : remoteEvents;

        if (eventsToCache.Count == 0)
        {
            _logger?.LogInformation("No events to cache (all within safe window)");
            return CacheSyncResult.Success(0, 0, CacheSyncAction.RebuiltFromScratch, stopwatch.Elapsed);
        }

        _logger?.LogInformation("Caching {Count} events...", eventsToCache.Count);

        var writeResult = await _localStore.WriteSerializableEventsAsync(eventsToCache);
        if (!writeResult.IsSuccess)
        {
            return CacheSyncResult.Failed(
                $"Failed to write events to cache: {writeResult.GetException().Message}",
                stopwatch.Elapsed);
        }

        // Update metadata
        await _localStore.SetMetadataAsync(new CacheMetadata
        {
            RemoteEndpoint = _options.RemoteEndpoint,
            DatabaseName = _options.DatabaseName,
            SchemaVersion = _options.SchemaVersion,
            TotalCountAtFetch = remoteCount,
            LastCachedSortableUniqueId = eventsToCache.Last().SortableUniqueIdValue,
            LastSafeWindowUtc = until?.GetDateTime(),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        });

        _logger?.LogInformation("Cache rebuilt: {Count} events cached", eventsToCache.Count);

        return CacheSyncResult.Success(
            eventsToCache.Count,
            eventsToCache.Count,
            CacheSyncAction.RebuiltFromScratch,
            stopwatch.Elapsed);
    }

    private bool ValidateMetadata(CacheMetadata? metadata)
    {
        if (metadata == null)
        {
            return false;
        }

        // Check if remote endpoint matches
        if (!string.IsNullOrEmpty(_options.RemoteEndpoint) &&
            metadata.RemoteEndpoint != _options.RemoteEndpoint)
        {
            _logger?.LogWarning("Remote endpoint changed: {Old} -> {New}",
                metadata.RemoteEndpoint, _options.RemoteEndpoint);
            return false;
        }

        // Check if database name matches
        if (!string.IsNullOrEmpty(_options.DatabaseName) &&
            metadata.DatabaseName != _options.DatabaseName)
        {
            _logger?.LogWarning("Database name changed: {Old} -> {New}",
                metadata.DatabaseName, _options.DatabaseName);
            return false;
        }

        // Check schema version
        if (!string.IsNullOrEmpty(_options.SchemaVersion) &&
            metadata.SchemaVersion != _options.SchemaVersion)
        {
            _logger?.LogWarning("Schema version changed: {Old} -> {New}",
                metadata.SchemaVersion, _options.SchemaVersion);
            return false;
        }

        return true;
    }

    private SortableUniqueId? GetSafeWindowThreshold()
    {
        if (_options.SafeWindow == TimeSpan.Zero)
        {
            return null;
        }

        var threshold = DateTime.UtcNow - _options.SafeWindow;
        return new SortableUniqueId(SortableUniqueId.Generate(threshold, Guid.Empty));
    }
}

/// <summary>
///     Cache statistics
/// </summary>
public record CacheStatistics
{
    public long EventCount { get; init; }
    public long DatabaseSizeBytes { get; init; }
    public string? RemoteEndpoint { get; init; }
    public string? DatabaseName { get; init; }
    public string? LastCachedSortableUniqueId { get; init; }
    public DateTime? LastSyncUtc { get; init; }
    public DateTime? CreatedUtc { get; init; }

    public string FormattedDatabaseSize
    {
        get
        {
            string[] sizes = ["B", "KB", "MB", "GB"];
            double len = DatabaseSizeBytes;
            var order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
