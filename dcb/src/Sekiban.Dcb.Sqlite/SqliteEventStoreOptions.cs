namespace Sekiban.Dcb.Sqlite;

/// <summary>
///     Configuration options for SqliteEventStore
/// </summary>
public class SqliteEventStoreOptions
{
    /// <summary>
    ///     Enable WAL (Write-Ahead Logging) mode for better concurrent access
    /// </summary>
    public bool UseWalMode { get; set; } = true;

    /// <summary>
    ///     Automatically create database and tables if they don't exist
    /// </summary>
    public bool AutoCreateDatabase { get; set; } = true;

    /// <summary>
    ///     Batch size for writing events
    /// </summary>
    public int BatchWriteSize { get; set; } = 1000;

    /// <summary>
    ///     Optional callback for progress reporting during long reads
    ///     Parameters: (eventsRead, percentComplete)
    /// </summary>
    public Action<int, double>? ReadProgressCallback { get; set; }
}
