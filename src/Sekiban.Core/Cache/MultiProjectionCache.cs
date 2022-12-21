using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Setting;
namespace Sekiban.Core.Cache;

/// <summary>
///     Default implementation of <see cref="IMultiProjectionCache" />.
///     Using dotnet memory cache.
/// </summary>
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
        where TProjectionPayload : IMultiProjectionPayloadCommon, new() =>
        _memoryCache.Get<MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>>(
            GetInMemoryKey<TProjection, TProjectionPayload>());

    private static MemoryCacheEntryOptions GetMemoryCacheOptions() => new()
    {
        AbsoluteExpiration = DateTimeOffset.UtcNow.AddHours(2),
        SlidingExpiration = TimeSpan.FromMinutes(15)
        // If not accessed 5 minutes it will be deleted. Anyway it will be deleted after two hours
    };

    private string GetInMemoryKey<TProjector, TPayload>() where TProjector : IMultiProjector<TPayload>, new()
        where TPayload : IMultiProjectionPayloadCommon, new()
    {
        var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
        return "MultiProjection-" + sekibanContext?.SettingGroupIdentifier + "-" + typeof(TProjector).FullName;
    }
}
