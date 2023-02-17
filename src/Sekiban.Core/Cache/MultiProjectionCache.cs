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
    private readonly IMemoryCacheAccessor _memoryCache;
    private readonly IMemoryCacheSettings _memoryCacheSettings;
    private readonly IServiceProvider _serviceProvider;
    public MultiProjectionCache(IMemoryCacheAccessor memoryCache, IServiceProvider serviceProvider, IMemoryCacheSettings memoryCacheSettings)
    {
        _memoryCache = memoryCache;
        _serviceProvider = serviceProvider;
        _memoryCacheSettings = memoryCacheSettings;
    }

    public void Set<TProjection, TProjectionPayload>(
        MultipleMemoryProjectionContainer<TProjection, TProjectionPayload> container)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        _memoryCache.Cache.Set(GetInMemoryKey<TProjection, TProjectionPayload>(), container, GetMemoryCacheOptions());
    }

    public MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>? Get<TProjection, TProjectionPayload>()
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        var toReturn = _memoryCache.Cache.Get<MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>>(
            GetInMemoryKey<TProjection, TProjectionPayload>());
        return toReturn;
    }

    private MemoryCacheEntryOptions GetMemoryCacheOptions()
    {
        return new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(_memoryCacheSettings.MultiProjectionAbsoluteExpirationMinutes),
            SlidingExpiration = TimeSpan.FromMinutes(_memoryCacheSettings.MultiProjectionSlidingExpirationMinutes)
            // If not accessed 5 minutes it will be deleted. Anyway it will be deleted after two hours
        };
    }

    private string GetInMemoryKey<TProjector, TPayload>() where TProjector : IMultiProjector<TPayload>, new()
        where TPayload : IMultiProjectionPayloadCommon, new()
    {
        var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
        var name = "MultiProjection-" + sekibanContext?.SettingGroupIdentifier + "-" + typeof(TPayload).FullName;
        return name;
    }
}
