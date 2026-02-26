namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Captures timing and size metrics from the snapshot persist pipeline.
/// </summary>
public sealed record SnapshotPersistMetrics(
    long SnapshotBuildMs,
    long SnapshotUploadMs,
    long TempFileSizeBytes,
    long PeakManagedMemoryBytes);
