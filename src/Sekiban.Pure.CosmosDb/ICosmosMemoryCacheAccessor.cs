using Microsoft.Extensions.Caching.Memory;

namespace Sekiban.Pure.CosmosDb;

/// <summary>
///     Use this to access memory cache instance.
///     In should share same instance over threads.
/// </summary>
public interface ICosmosMemoryCacheAccessor
{
    /// <summary>
    ///     Get shared memory cache instance.
    /// </summary>
    IMemoryCache Cache { get; }
}