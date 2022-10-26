using Microsoft.Extensions.Caching.Memory;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleAggregate;
using Sekiban.Core.Query.SingleAggregate.SingleProjection;
namespace Sekiban.Core.Cache;

public class SingleAggregateProjectionCache : ISingleAggregateProjectionCache
{
    private readonly IMemoryCache _memoryCache;
    public SingleAggregateProjectionCache(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
    }

    public void SetContainer<TAggregate, TState>(Guid aggregateId, SingleMemoryCacheProjectionContainer<TAggregate, TState> container)
        where TAggregate : ISingleAggregate, ISingleAggregateProjection where TState : ISingleAggregate
    {
        _memoryCache.Set(
            GetCacheKeyForSingleProjectionContainer<TAggregate>(aggregateId),
            container,
            GetMemoryCacheOptionsForSingleProjectionContainer());
    }
    public SingleMemoryCacheProjectionContainer<TAggregate, TState>? GetContainer<TAggregate, TState>(Guid aggregateId)
        where TAggregate : ISingleAggregate, ISingleAggregateProjection where TState : ISingleAggregate
    {
        return _memoryCache.Get<SingleMemoryCacheProjectionContainer<TAggregate, TState>>(
            GetCacheKeyForSingleProjectionContainer<TAggregate>(aggregateId));
    }

    private static MemoryCacheEntryOptions GetMemoryCacheOptionsForSingleProjectionContainer()
    {
        return new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = DateTimeOffset.UtcNow.AddHours(2), SlidingExpiration = TimeSpan.FromMinutes(15)
            // 5分読まれなかったら削除するが、2時間経ったらどちらにしても削除する
        };
    }
    public string GetCacheKeyForSingleProjectionContainer<TAggregate>(Guid aggregateId)
    {
        if (typeof(TAggregate).IsGenericType && typeof(TAggregate).GetGenericTypeDefinition() == typeof(Aggregate<>))
        {
            return $"{typeof(TAggregate).GetGenericArguments()[0].Name}_{aggregateId}";
        }
        return "SingleAggregate" + typeof(TAggregate).Name + aggregateId;
    }
}
