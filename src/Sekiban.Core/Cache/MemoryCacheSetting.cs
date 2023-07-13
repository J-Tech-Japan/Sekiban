using Microsoft.Extensions.Configuration;
namespace Sekiban.Core.Cache;

/// <summary>
///     Set memory cache setting.
///     Can be used with appsettings.json and set value manually as well.
/// </summary>
public class MemoryCacheSetting : IMemoryCacheSettings
{
    public MemoryCacheSetting(IConfiguration configuration)
    {
        SingleProjectionAbsoluteExpirationMinutes
            = configuration.GetValue<int?>("Sekiban:MemoryCache:SingleProjection:AbsoluteExpirationMinutes") ?? 120;
        SingleProjectionSlidingExpirationMinutes
            = configuration.GetValue<int?>("Sekiban:MemoryCache:SingleProjection:SlidingExpirationMinutes") ?? 15;
        SnapshotAbsoluteExpirationMinutes = configuration.GetValue<int?>("Sekiban:MemoryCache:Snapshot:AbsoluteExpirationMinutes") ?? 120;
        SnapshotSlidingExpirationMinutes = configuration.GetValue<int?>("Sekiban:MemoryCache:Snapshot:SlidingExpirationMinutes") ?? 15;
        MultiProjectionAbsoluteExpirationMinutes
            = configuration.GetValue<int?>("Sekiban:MemoryCache:MultiProjection:AbsoluteExpirationMinutes") ?? 120;
        MultiProjectionSlidingExpirationMinutes = configuration.GetValue<int?>("Sekiban:MemoryCache:MultiProjection:SlidingExpirationMinutes") ?? 15;
    }

    public MemoryCacheSetting()
    {
    }

    public int SingleProjectionAbsoluteExpirationMinutes { get; init; } = 120;
    public int SingleProjectionSlidingExpirationMinutes { get; init; } = 15;
    public int SnapshotAbsoluteExpirationMinutes { get; init; } = 120;
    public int SnapshotSlidingExpirationMinutes { get; init; } = 15;
    public int MultiProjectionAbsoluteExpirationMinutes { get; init; } = 120;
    public int MultiProjectionSlidingExpirationMinutes { get; init; } = 15;
}
