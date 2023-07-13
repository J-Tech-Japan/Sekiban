using Microsoft.Extensions.Caching.Memory;
namespace Sekiban.Core.Cache;

/// <summary>
///     Memory cache accessor
///     Note: This class is for internal use only
/// </summary>
public class MemoryCacheAccessor : IMemoryCacheAccessor
{
    private static IMemoryCache? staticMemoryCache;
    public MemoryCacheAccessor(IMemoryCache memoryCache) => Cache = staticMemoryCache ??= memoryCache;
    public IMemoryCache Cache { get; }
}
