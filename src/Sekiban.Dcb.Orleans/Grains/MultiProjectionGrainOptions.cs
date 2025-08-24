namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Options for multi-projection grain configuration
/// </summary>
public class MultiProjectionGrainOptions
{
    /// <summary>
    ///     Maximum state size in bytes (default: 2MB)
    /// </summary>
    public int MaxStateSize { get; set; } = 2 * 1024 * 1024;

    /// <summary>
    ///     Interval for automatic state persistence (default: 5 minutes)
    /// </summary>
    public TimeSpan PersistInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Safe window duration for event ordering (default: 20 seconds)
    /// </summary>
    public TimeSpan SafeWindowDuration { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    ///     Batch size for event processing (default: 1000)
    /// </summary>
    public int EventBatchSize { get; set; } = 1000;

    /// <summary>
    ///     Use memory storage for development/testing (default: false)
    /// </summary>
    public bool UseMemoryStorage { get; set; } = false;

    /// <summary>
    ///     Storage provider name (default: "OrleansStorage")
    /// </summary>
    public string StorageProviderName { get; set; } = "OrleansStorage";
}
