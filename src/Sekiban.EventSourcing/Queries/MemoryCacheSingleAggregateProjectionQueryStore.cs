using Microsoft.Extensions.Caching.Memory;
namespace Sekiban.EventSourcing.Queries;

public class MemoryCacheSingleAggregateProjectionQueryStore : ISingleAggregateProjectionQueryStore
{
    private readonly IMemoryCache _memoryCache;
    public MemoryCacheSingleAggregateProjectionQueryStore(IMemoryCache memoryCache) =>
        _memoryCache = memoryCache;
    public void SaveProjection(ISingleAggregate aggregate, string typeName)
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = DateTimeOffset.UtcNow.AddHours(2),
            SlidingExpiration = TimeSpan.FromMinutes(5)
            // 5分読まれなかったら削除するが、2時間経ったらどちらにしても削除する
        };
        _memoryCache.Set(AggregateUniqueKey(aggregate.AggregateId, typeName), aggregate, options);
    }
    public TAggregate? FindAggregate<TAggregate>(Guid aggregateId, string typeName) where TAggregate : ISingleAggregate
    {
        if (_memoryCache.TryGetValue(AggregateUniqueKey(aggregateId, typeName), out var content))
        {
            return (TAggregate)content;
        }
        return default;
    }
    public void SaveLatestAggregateList<T>(SingleAggregateList<T> singleAggregateList) where T : ISingleAggregate
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = DateTimeOffset.UtcNow.AddHours(2),
            SlidingExpiration = TimeSpan.FromMinutes(5)
            // 5分読まれなかったら削除するが、2時間経ったらどちらにしても削除する
        };
        _memoryCache.Set(SingleAggregateList<T>.UniqueKey(), singleAggregateList, options);
    }
    public SingleAggregateList<T>? FindAggregateList<T>() where T : ISingleAggregate
    {
        if (_memoryCache.TryGetValue(SingleAggregateList<T>.UniqueKey(), out var content))
        {
            return (SingleAggregateList<T>)content;
        }
        return null;
    }

    private string AggregateUniqueKey(Guid aggregateId, string typeName) =>
        $"SAP-{typeName}-{aggregateId}";
}
