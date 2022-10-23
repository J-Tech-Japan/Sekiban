using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.MultipleAggregate.MultipleProjection;
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
    public void Set<TProjection, TContents>(MultipleMemoryProjectionContainer<TProjection, TContents> container) where TProjection : IMultipleAggregateProjector<TContents>, new() where TContents : IMultipleAggregateProjectionContents, new()
    {
        _memoryCache.Set(GetInMemoryKey<TProjection, TContents>(), container, GetMemoryCacheOptions());
    }
    public MultipleMemoryProjectionContainer<TProjection, TContents> Get<TProjection, TContents>() where TProjection : IMultipleAggregateProjector<TContents>, new() where TContents : IMultipleAggregateProjectionContents, new()
    {
        return _memoryCache.Get<MultipleMemoryProjectionContainer<TProjection, TContents>>(
            GetInMemoryKey<TProjection, TContents>());
    }
    
    private static MemoryCacheEntryOptions GetMemoryCacheOptions()
    {
        return new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = DateTimeOffset.UtcNow.AddHours(2), SlidingExpiration = TimeSpan.FromMinutes(15)
            // 5分読まれなかったら削除するが、2時間経ったらどちらにしても削除する
        };
    }
    private string GetInMemoryKey<P, Q>() where P : IMultipleAggregateProjector<Q>, new() where Q : IMultipleAggregateProjectionContents, new()
    {
        var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
        return "MultipleProjection-" + sekibanContext?.SettingGroupIdentifier + "-" + typeof(P).FullName;
    }
}
