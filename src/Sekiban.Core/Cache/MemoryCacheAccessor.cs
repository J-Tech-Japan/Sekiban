using Microsoft.Extensions.Caching.Memory;
namespace Sekiban.Core.Cache;

public class MemoryCacheAccessor : IMemoryCacheAccessor
{
    private readonly IMemoryCache _memoryCache;
    private static IMemoryCache? staticMemoryCache;
    public MemoryCacheAccessor(IMemoryCache memoryCache)
    {
        _memoryCache = staticMemoryCache is null? staticMemoryCache = memoryCache : memoryCache;
    }
    public IMemoryCache Cache => _memoryCache;
}
