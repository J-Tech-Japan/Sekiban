using Microsoft.Extensions.Caching.Memory;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Query.SingleProjections.Projections;
using Sekiban.Core.Types;
using ISingleProjection = Sekiban.Core.Query.SingleProjections.ISingleProjection;
namespace Sekiban.Core.Cache;

/// <summary>
///     Default implementation of <see cref="ISingleProjectionCache" />.
///     Using dotnet memory cache.
/// </summary>
public class SingleProjectionCache : ISingleProjectionCache
{
    private readonly IMemoryCacheAccessor _memoryCache;
    private readonly MemoryCacheSetting _memoryCacheSettings;

    public SingleProjectionCache(IMemoryCacheAccessor memoryCache, MemoryCacheSetting memoryCacheSettings)
    {
        _memoryCache = memoryCache;
        _memoryCacheSettings = memoryCacheSettings;
    }

    public void SetContainer<TAggregate, TState>(
        Guid aggregateId,
        SingleMemoryCacheProjectionContainer<TAggregate, TState> container)
        where TAggregate : IAggregateCommon, ISingleProjection where TState : IAggregateStateCommon
    {
        _memoryCache.Cache.Set(
            GetCacheKeyForSingleProjectionContainer<TAggregate>(aggregateId),
            container,
            GetMemoryCacheOptionsForSingleProjectionContainer());
    }

    public SingleMemoryCacheProjectionContainer<TAggregate, TState>? GetContainer<TAggregate, TState>(Guid aggregateId)
        where TAggregate : IAggregateCommon, ISingleProjection where TState : IAggregateStateCommon =>
        _memoryCache.Cache.Get<SingleMemoryCacheProjectionContainer<TAggregate, TState>>(
            GetCacheKeyForSingleProjectionContainer<TAggregate>(aggregateId));

    private MemoryCacheEntryOptions GetMemoryCacheOptionsForSingleProjectionContainer() =>
        new()
        {
            AbsoluteExpiration
                = DateTimeOffset.UtcNow.AddMinutes(_memoryCacheSettings.SingleProjection.AbsoluteExpirationMinutes),
            SlidingExpiration = TimeSpan.FromMinutes(_memoryCacheSettings.SingleProjection.SlidingExpirationMinutes)
            // If not accessed 5 minutes it will be deleted. Anyway it will be d
            // eleted after two hours
        };

    public static string GetCacheKeyForSingleProjectionContainer<TSingleProjectionOrAggregate>(Guid aggregateId) =>
        typeof(TSingleProjectionOrAggregate).IsSingleProjectionType()
            ? $"{typeof(TSingleProjectionOrAggregate).GetSingleProjectionPayloadFromSingleProjectionType().GetAggregatePayloadTypeFromSingleProjectionPayload().Name}_{aggregateId}"
            : "Aggregate" +
            typeof(TSingleProjectionOrAggregate).GetAggregatePayloadTypeFromAggregate().Name +
            aggregateId;
}
