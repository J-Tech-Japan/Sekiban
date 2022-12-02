using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Setting;

namespace Sekiban.Core.Cache;

public class MultiProjectionCache : IMultiProjectionCache
{
    private readonly IMemoryCache _memoryCache;
    private readonly IServiceProvider _serviceProvider;

    public MultiProjectionCache(IMemoryCache memoryCache, IServiceProvider serviceProvider)
    {
        _memoryCache = memoryCache;
        _serviceProvider = serviceProvider;
    }

    public void Set<TProjection, TProjectionPayload>(
        MultipleMemoryProjectionContainer<TProjection, TProjectionPayload> container)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        _memoryCache.Set(GetInMemoryKey<TProjection, TProjectionPayload>(), container, GetMemoryCacheOptions());
    }

    public MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>? Get<TProjection, TProjectionPayload>()
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        return _memoryCache.Get<MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>>(
            GetInMemoryKey<TProjection, TProjectionPayload>());
    }

    private static MemoryCacheEntryOptions GetMemoryCacheOptions()
    {
        return new()
        {
            AbsoluteExpiration = DateTimeOffset.UtcNow.AddHours(2),
            SlidingExpiration = TimeSpan.FromMinutes(15)
            // 5分読まれなかったら削除するが、2時間経ったらどちらにしても削除する
        };
    }

    private string GetInMemoryKey<TProjector, TPayload>() where TProjector : IMultiProjector<TPayload>, new()
        where TPayload : IMultiProjectionPayloadCommon, new()
    {
        var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
        return "MultiProjection-" + sekibanContext?.SettingGroupIdentifier + "-" + typeof(TProjector).FullName;
    }
}
