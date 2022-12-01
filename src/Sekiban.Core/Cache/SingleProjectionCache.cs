using Microsoft.Extensions.Caching.Memory;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Query.SingleProjections.Projections;
using Sekiban.Core.Types;
using ISingleProjection = Sekiban.Core.Query.SingleProjections.ISingleProjection;

namespace Sekiban.Core.Cache;

public class SingleProjectionCache : ISingleProjectionCache
{
    private readonly IMemoryCache _memoryCache;

    public SingleProjectionCache(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
    }

    public void SetContainer<TAggregate, TState>(Guid aggregateId,
        SingleMemoryCacheProjectionContainer<TAggregate, TState> container)
        where TAggregate : IAggregateCommon, ISingleProjection where TState : IAggregateCommon
    {
        _memoryCache.Set(
            GetCacheKeyForSingleProjectionContainer<TAggregate>(aggregateId),
            container,
            GetMemoryCacheOptionsForSingleProjectionContainer());
    }

    public SingleMemoryCacheProjectionContainer<TAggregate, TState>? GetContainer<TAggregate, TState>(Guid aggregateId)
        where TAggregate : IAggregateCommon, ISingleProjection where TState : IAggregateCommon
    {
        return _memoryCache.Get<SingleMemoryCacheProjectionContainer<TAggregate, TState>>(
            GetCacheKeyForSingleProjectionContainer<TAggregate>(aggregateId));
    }

    private static MemoryCacheEntryOptions GetMemoryCacheOptionsForSingleProjectionContainer()
    {
        return new()
        {
            AbsoluteExpiration = DateTimeOffset.UtcNow.AddHours(2),
            SlidingExpiration = TimeSpan.FromMinutes(15)
            // 5分読まれなかったら削除するが、2時間経ったらどちらにしても削除する
        };
    }

    public string GetCacheKeyForSingleProjectionContainer<TSingleProjectionOrAggregate>(Guid aggregateId)
    {
        if (typeof(TSingleProjectionOrAggregate).IsSingleProjectionType())
            return
                $"{typeof(TSingleProjectionOrAggregate).GetSingleProjectionPayloadFromSingleProjectionType().GetOriginalTypeFromSingleProjectionPayload().Name}_{aggregateId}";
        return "Aggregate" + typeof(TSingleProjectionOrAggregate).GetAggregatePayloadTypeFromAggregate().Name +
               aggregateId;
    }
}
