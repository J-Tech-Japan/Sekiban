using Microsoft.Extensions.Caching.Memory;
using Sekiban.Core.Partition;
using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Cache;

/// <summary>
///     Default implementation of <see cref="ISnapshotDocumentCache" />.
///     Using dotnet memory cache.
/// </summary>
public class SnapshotDocumentCache : ISnapshotDocumentCache
{
    private readonly IMemoryCacheAccessor _memoryCache;
    private readonly MemoryCacheSetting _memoryCacheSettings;
    public SnapshotDocumentCache(IMemoryCacheAccessor memoryCache, MemoryCacheSetting memoryCacheSettings)
    {
        _memoryCache = memoryCache;
        _memoryCacheSettings = memoryCacheSettings;
    }

    public void Set(SnapshotDocument document)
    {
        _memoryCache.Cache.Set(SnapshotDocumentCache.GetCacheKey(document), document, GetMemoryCacheOptions());
    }

    public SnapshotDocument? Get(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, string rootPartitionKey) =>
        _memoryCache.Cache.Get<SnapshotDocument>(SnapshotDocumentCache.GetCacheKey(aggregateId, aggregatePayloadType, projectionPayloadType, rootPartitionKey));

    public static string GetCacheKey(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, string rootPartitionKey) =>
        "SnapshotDocument" + PartitionKeyGenerator.ForAggregateSnapshot(aggregateId, aggregatePayloadType, projectionPayloadType, rootPartitionKey);
    private MemoryCacheEntryOptions GetMemoryCacheOptions() =>
        new()
        {
            AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(_memoryCacheSettings.Snapshot.AbsoluteExpirationMinutes),
            SlidingExpiration = TimeSpan.FromMinutes(_memoryCacheSettings.Snapshot.SlidingExpirationMinutes)
            // If not accessed 5 minutes it will be deleted. Anyway it will be deleted after two hours
        };
    public static string GetCacheKey(SnapshotDocument document) => "SnapshotDocument" + document.PartitionKey;
}
