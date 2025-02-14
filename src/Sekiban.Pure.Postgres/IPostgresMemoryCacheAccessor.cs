using Microsoft.Extensions.Caching.Memory;

namespace Sekiban.Pure.Postgres;

/// <summary>
///     Use this to access memory cache instance.
///     In should share same instance over threads.
/// </summary>
public interface IPostgresMemoryCacheAccessor
{
    /// <summary>
    ///     Get shared memory cache instance.
    /// </summary>
    IMemoryCache Cache { get; }
}