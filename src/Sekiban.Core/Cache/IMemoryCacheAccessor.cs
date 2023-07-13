using Microsoft.Extensions.Caching.Memory;
namespace Sekiban.Core.Cache;

/// <summary>
///     Use this to access memory cache instance.
///     In should share same instance over threads.
/// </summary>
public interface IMemoryCacheAccessor
{
    /// <summary>
    ///     Get shared memory cache instance.
    /// </summary>
    IMemoryCache Cache { get; }
}
