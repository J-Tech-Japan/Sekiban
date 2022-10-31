using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Query.MultipleProjections;
using Sekiban.Core.Query.MultipleProjections.Projections;
using Sekiban.Core.Setting;
namespace Sekiban.Core.Cache;

public class MultipleAggregateProjectionCache : IMultipleAggregateProjectionCache
{
    private readonly IMemoryCache _memoryCache;
    private readonly IServiceProvider _serviceProvider;

    public MultipleAggregateProjectionCache(IMemoryCache memoryCache, IServiceProvider serviceProvider)
    {
        _memoryCache = memoryCache;
        _serviceProvider = serviceProvider;
    }
    public void Set<TProjection, TProjectionPayload>(MultipleMemoryProjectionContainer<TProjection, TProjectionPayload> container)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
    {
        _memoryCache.Set(GetInMemoryKey<TProjection, TProjectionPayload>(), container, GetMemoryCacheOptions());
    }
    public MultipleMemoryProjectionContainer<TProjection, TProjectionPayload> Get<TProjection, TProjectionPayload>()
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new() =>
        _memoryCache.Get<MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>>(
            GetInMemoryKey<TProjection, TProjectionPayload>());

    private static MemoryCacheEntryOptions GetMemoryCacheOptions() => new MemoryCacheEntryOptions
    {
        AbsoluteExpiration = DateTimeOffset.UtcNow.AddHours(2), SlidingExpiration = TimeSpan.FromMinutes(15)
        // 5分読まれなかったら削除するが、2時間経ったらどちらにしても削除する
    };
    private string GetInMemoryKey<P, Q>() where P : IMultiProjector<Q>, new() where Q : IMultiProjectionPayload, new()
    {
        var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
        return "MultipleProjection-" + sekibanContext?.SettingGroupIdentifier + "-" + typeof(P).FullName;
    }
}
