namespace Sekiban.Dcb.Sqlite;

/// <summary>
///     Configuration options for cache synchronization
/// </summary>
public class CacheSyncOptions
{
    /// <summary>
    ///     Time window to exclude from caching.
    ///     Events within this window are considered "unsafe" and will be fetched from remote.
    /// </summary>
    public TimeSpan SafeWindow { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     Remote endpoint identifier (for cache key construction and validation)
    /// </summary>
    public string RemoteEndpoint { get; set; } = "";

    /// <summary>
    ///     Database name (for cache key construction and validation)
    /// </summary>
    public string DatabaseName { get; set; } = "";

    /// <summary>
    ///     Schema version for cache invalidation
    /// </summary>
    public string SchemaVersion { get; set; } = "1.0";
}
