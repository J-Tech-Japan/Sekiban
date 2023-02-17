using Microsoft.Extensions.Caching.Memory;
namespace Sekiban.Core.Cache;

public class MemoryCacheAccessor : IMemoryCacheAccessor
{
    private static IMemoryCache? staticMemoryCache;
    public MemoryCacheAccessor(IMemoryCache memoryCache)
    {
        Cache = staticMemoryCache is null ? staticMemoryCache = memoryCache : staticMemoryCache;
    }
    public IMemoryCache Cache
    {
        get;
    }
}
