namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Configuration options for temp file-based snapshot persistence.
/// </summary>
public class SnapshotTempFileOptions
{
    /// <summary>
    ///     Directory where temporary snapshot files are created.
    /// </summary>
    public string TempDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "sekiban-snapshots");

    /// <summary>
    ///     Maximum number of concurrent temp files allowed.
    /// </summary>
    public int MaxConcurrentFiles { get; set; } = 10;

    /// <summary>
    ///     Maximum total size in bytes across all temp files.
    /// </summary>
    public long MaxTotalSizeBytes { get; set; } = 500L * 1024 * 1024;

    /// <summary>
    ///     Minutes after which an unreferenced temp file is considered stale.
    /// </summary>
    public int StaleFileTimeoutMinutes { get; set; } = 30;
}
