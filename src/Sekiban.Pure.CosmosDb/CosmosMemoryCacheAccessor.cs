using Microsoft.Extensions.Caching.Memory;

namespace Sekiban.Pure.CosmosDb;

/// <summary>
///     Memory cache accessor
///     Note: This class is for internal use only
/// </summary>
public class CosmosMemoryCacheAccessor : ICosmosMemoryCacheAccessor
{
    private static IMemoryCache? staticMemoryCache;
    public CosmosMemoryCacheAccessor(IMemoryCache memoryCache) => Cache = staticMemoryCache ??= memoryCache;
    public IMemoryCache Cache { get; }
}