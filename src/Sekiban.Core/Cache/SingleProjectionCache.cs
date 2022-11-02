using Microsoft.Extensions.Caching.Memory;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Query.SingleProjections.Projections;
using ISingleProjection = Sekiban.Core.Query.SingleProjections.ISingleProjection;
namespace Sekiban.Core.Cache;

public class SingleProjectionCache : ISingleProjectionCache
{
    private readonly IMemoryCache _memoryCache;
    public SingleProjectionCache(IMemoryCache memoryCache) => _memoryCache = memoryCache;

    public void SetContainer<TAggregate, TState>(Guid aggregateId, SingleMemoryCacheProjectionContainer<TAggregate, TState> container)
        where TAggregate : IAggregateIdentifier, ISingleProjection where TState : IAggregateIdentifier
    {
        _memoryCache.Set(
            GetCacheKeyForSingleProjectionContainer<TAggregate>(aggregateId),
            container,
            GetMemoryCacheOptionsForSingleProjectionContainer());
    }
    public SingleMemoryCacheProjectionContainer<TAggregate, TState>? GetContainer<TAggregate, TState>(Guid aggregateId)
        where TAggregate : IAggregateIdentifier, ISingleProjection where TState : IAggregateIdentifier =>
        _memoryCache.Get<SingleMemoryCacheProjectionContainer<TAggregate, TState>>(
            GetCacheKeyForSingleProjectionContainer<TAggregate>(aggregateId));

    private static MemoryCacheEntryOptions GetMemoryCacheOptionsForSingleProjectionContainer() => new MemoryCacheEntryOptions
    {
        AbsoluteExpiration = DateTimeOffset.UtcNow.AddHours(2), SlidingExpiration = TimeSpan.FromMinutes(15)
        // 5分読まれなかったら削除するが、2時間経ったらどちらにしても削除する
    };
    public string GetCacheKeyForSingleProjectionContainer<TAggregate>(Guid aggregateId)
    {
        if (typeof(TAggregate).IsGenericType && typeof(TAggregate).GetGenericTypeDefinition() == typeof(AggregateIdentifier<>))
        {
            return $"{typeof(TAggregate).GetGenericArguments()[0].Name}_{aggregateId}";
        }
        return "Aggregate" + typeof(TAggregate).Name + aggregateId;
    }
}
