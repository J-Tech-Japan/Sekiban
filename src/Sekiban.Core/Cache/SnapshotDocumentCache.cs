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

    public SnapshotDocumentCache(IMemoryCache memoryCache) => _memoryCache = memoryCache;

    public void Set(SnapshotDocument document)
    {
        _memoryCache.Set(GetCacheKey(document), document);
    }

    public SnapshotDocument? Get(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType) =>
        _memoryCache.Get<SnapshotDocument>(GetCacheKey(aggregateId, aggregatePayloadType, projectionPayloadType));

    public string GetCacheKey(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType) => "SnapshotDocument" +
        PartitionKeyGenerator.ForAggregateSnapshot(aggregateId, aggregatePayloadType, projectionPayloadType);

    public string GetCacheKey(SnapshotDocument document) => "SnapshotDocument" + document.PartitionKey;
}
