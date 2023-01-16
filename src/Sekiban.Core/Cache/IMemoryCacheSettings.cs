namespace Sekiban.Core.Cache;

public interface IMemoryCacheSettings
{
    public int SingleProjectionAbsoluteExpirationMinutes { get; }
    public int SingleProjectionSlidingExpirationMinutes { get; }
    public int SnapshotAbsoluteExpirationMinutes { get; }
    public int SnapshotSlidingExpirationMinutes { get; }
    public int MultiProjectionAbsoluteExpirationMinutes { get; }
    public int MultiProjectionSlidingExpirationMinutes { get; }
}
