namespace Sekiban.Core.Cache;

/// <summary>
///     Memory Cache Settings, if you use default implementation, you can use <see cref="MemoryCacheSetting" />
///     appsettings.json can be used to configure this.
/// </summary>
public interface IMemoryCacheSettings
{
    /// <summary>
    ///     Absolute Expiration Minutes for Single Projection Cache
    /// </summary>
    public int SingleProjectionAbsoluteExpirationMinutes { get; }
    /// <summary>
    ///     Sliding Expiration Minutes for Single Projection Cache
    /// </summary>
    public int SingleProjectionSlidingExpirationMinutes { get; }
    /// <summary>
    ///     Absolute Expiration Minutes for Snapshot Cache
    /// </summary>
    public int SnapshotAbsoluteExpirationMinutes { get; }
    /// <summary>
    ///     Absolute Expiration Minutes for Snapshot Cache
    /// </summary>
    public int SnapshotSlidingExpirationMinutes { get; }
    /// <summary>
    ///     Absolute Expiration Minutes for Multi Projection Cache
    /// </summary>
    public int MultiProjectionAbsoluteExpirationMinutes { get; }
    /// <summary>
    ///     Sliding Expiration Minutes for Multi Projection Cache
    /// </summary>
    public int MultiProjectionSlidingExpirationMinutes { get; }
}
