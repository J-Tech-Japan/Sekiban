using Microsoft.Extensions.Caching.Memory;
namespace Sekiban.Core.Cache;

public class MemoryCacheAccessor : IMemoryCacheAccessor
{
    private static IMemoryCache? staticMemoryCache;
    public MemoryCacheAccessor(IMemoryCache memoryCache)
    {
        Cache = staticMemoryCache ??= memoryCache;
    }
    public IMemoryCache Cache
    {
        get;
    }
}
