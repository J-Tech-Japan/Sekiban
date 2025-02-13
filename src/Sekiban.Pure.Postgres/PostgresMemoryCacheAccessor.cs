using Microsoft.Extensions.Caching.Memory;

namespace Sekiban.Pure.Postgres;

public class PostgresMemoryCacheAccessor : IPostgresMemoryCacheAccessor
{
    private static IMemoryCache? staticMemoryCache;

    public PostgresMemoryCacheAccessor(IMemoryCache memoryCache)
    {
        Cache = staticMemoryCache ??= memoryCache;
    }

    public IMemoryCache Cache { get; }
}