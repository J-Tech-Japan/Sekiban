using Microsoft.Extensions.Caching.Memory;
using Sekiban.Core.Partition;
using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Cache;

public class SnapshotDocumentCache : ISnapshotDocumentCache
{
    private readonly IMemoryCache _memoryCache;
    public SnapshotDocumentCache(IMemoryCache memoryCache) => _memoryCache = memoryCache;

    public void Set(SnapshotDocument document)
    {
        _memoryCache.Set(GetCacheKey(document), document);
    }
    public SnapshotDocument? Get(Guid aggregateId, Type originalAggregateType) =>
        _memoryCache.Get<SnapshotDocument>(GetCacheKey(aggregateId, originalAggregateType));

    public string GetCacheKey(Guid aggregateId, Type aggregateType) =>
        "SnapshotDocument" + PartitionKeyGenerator.ForAggregateSnapshot(aggregateId, aggregateType);
    public string GetCacheKey(SnapshotDocument document) => "SnapshotDocument" + document.PartitionKey;
}
