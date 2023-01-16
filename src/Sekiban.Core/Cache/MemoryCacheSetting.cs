using Microsoft.Extensions.Configuration;
namespace Sekiban.Core.Cache;

public class MemoryCacheSetting : IMemoryCacheSettings
{
    public MemoryCacheSetting(IConfiguration configuration)
    {
        SingleProjectionAbsoluteExpirationMinutes =
            configuration.GetValue<int?>("Sekiban:MemoryCache:SingleProjection:AbsoluteExpirationMinutes") ?? 120;
        SingleProjectionSlidingExpirationMinutes =
            configuration.GetValue<int?>("Sekiban:MemoryCache:SingleProjection:SlidingExpirationMinutes") ?? 15;
        SnapshotAbsoluteExpirationMinutes = configuration.GetValue<int?>("Sekiban:MemoryCache:Snapshot:AbsoluteExpirationMinutes") ?? 120;
        SnapshotSlidingExpirationMinutes = configuration.GetValue<int?>("Sekiban:MemoryCache:Snapshot:SlidingExpirationMinutes") ?? 15;
        MultiProjectionAbsoluteExpirationMinutes =
            configuration.GetValue<int?>("Sekiban:MemoryCache:MultiProjection:AbsoluteExpirationMinutes") ?? 120;
        MultiProjectionSlidingExpirationMinutes = configuration.GetValue<int?>("Sekiban:MemoryCache:MultiProjection:SlidingExpirationMinutes") ?? 15;
    }

    public MemoryCacheSetting() { }

    public int SingleProjectionAbsoluteExpirationMinutes
    {
        get;
        init;
    } = 120;
    public int SingleProjectionSlidingExpirationMinutes
    {
        get;
        set;
    } = 15;
    public int SnapshotAbsoluteExpirationMinutes
    {
        get;
        set;
    } = 120;
    public int SnapshotSlidingExpirationMinutes
    {
        get;
        set;
    } = 15;
    public int MultiProjectionAbsoluteExpirationMinutes
    {
        get;
        set;
    } = 120;
    public int MultiProjectionSlidingExpirationMinutes
    {
        get;
        set;
    } = 15;
}
