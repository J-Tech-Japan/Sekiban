using Microsoft.Extensions.Caching.Memory;
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
    private readonly IMemoryCache _memoryCache;
    private readonly IMemoryCacheSettings _memoryCacheSettings;

    public SingleProjectionCache(IMemoryCache memoryCache, IMemoryCacheSettings memoryCacheSettings)
    {
        _memoryCache = memoryCache;
        _memoryCacheSettings = memoryCacheSettings;
    }

    public void SetContainer<TAggregate, TState>(
        Guid aggregateId,
        SingleMemoryCacheProjectionContainer<TAggregate, TState> container)
        where TAggregate : IAggregateCommon, ISingleProjection where TState : IAggregateCommon
    {
        _memoryCache.Set(
            GetCacheKeyForSingleProjectionContainer<TAggregate>(aggregateId),
            container,
            GetMemoryCacheOptionsForSingleProjectionContainer());
    }

    public SingleMemoryCacheProjectionContainer<TAggregate, TState>? GetContainer<TAggregate, TState>(Guid aggregateId)
        where TAggregate : IAggregateCommon, ISingleProjection where TState : IAggregateCommon =>
        _memoryCache.Get<SingleMemoryCacheProjectionContainer<TAggregate, TState>>(
            GetCacheKeyForSingleProjectionContainer<TAggregate>(aggregateId));

    private MemoryCacheEntryOptions GetMemoryCacheOptionsForSingleProjectionContainer() => new()
    {
        AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(_memoryCacheSettings.SingleProjectionAbsoluteExpirationMinutes),
        SlidingExpiration = TimeSpan.FromMinutes(_memoryCacheSettings.SingleProjectionSlidingExpirationMinutes),
        Size = 2000
        // If not accessed 5 minutes it will be deleted. Anyway it will be d
        // eleted after two hours
    };

    public string GetCacheKeyForSingleProjectionContainer<TSingleProjectionOrAggregate>(Guid aggregateId)
    {
        if (typeof(TSingleProjectionOrAggregate).IsSingleProjectionType())
        {
            return
                $"{typeof(TSingleProjectionOrAggregate).GetSingleProjectionPayloadFromSingleProjectionType().GetOriginalTypeFromSingleProjectionPayload().Name}_{aggregateId}";
        }
        return "Aggregate" +
            typeof(TSingleProjectionOrAggregate).GetAggregatePayloadTypeFromAggregate().Name +
            aggregateId;
    }
}
