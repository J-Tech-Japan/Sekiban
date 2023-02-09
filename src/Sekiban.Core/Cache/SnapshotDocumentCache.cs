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
    private readonly IMemoryCache _memoryCache;
    private readonly IMemoryCacheSettings _memoryCacheSettings;
    public SnapshotDocumentCache(IMemoryCache memoryCache, IMemoryCacheSettings memoryCacheSettings)
    {
        _memoryCache = memoryCache;
        _memoryCacheSettings = memoryCacheSettings;
    }

    public void Set(SnapshotDocument document)
    {
        _memoryCache.Set(GetCacheKey(document), document, GetMemoryCacheOptions());
    }

    public SnapshotDocument? Get(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType)
    {
        return _memoryCache.Get<SnapshotDocument>(GetCacheKey(aggregateId, aggregatePayloadType, projectionPayloadType));
    }

    public string GetCacheKey(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType)
    {
        return "SnapshotDocument" +
            PartitionKeyGenerator.ForAggregateSnapshot(aggregateId, aggregatePayloadType, projectionPayloadType);
    }
    private MemoryCacheEntryOptions GetMemoryCacheOptions()
    {
        return new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = DateTimeOffset.UtcNow.AddHours(_memoryCacheSettings.SnapshotAbsoluteExpirationMinutes),
            SlidingExpiration = TimeSpan.FromMinutes(_memoryCacheSettings.SnapshotSlidingExpirationMinutes)
            // If not accessed 5 minutes it will be deleted. Anyway it will be deleted after two hours
        };
    }
    public string GetCacheKey(SnapshotDocument document)
    {
        return "SnapshotDocument" + document.PartitionKey;
    }
}
