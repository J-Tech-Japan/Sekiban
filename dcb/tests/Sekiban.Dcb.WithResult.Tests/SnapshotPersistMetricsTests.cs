using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Unit tests for SnapshotPersistMetrics — a record capturing
///     timing and size metrics from the snapshot persist pipeline.
/// </summary>
public class SnapshotPersistMetricsTests
{
    [Fact]
    public void Should_Store_All_Metric_Values()
    {
        // When
        var metrics = new SnapshotPersistMetrics(
            SnapshotBuildMs: 150,
            SnapshotUploadMs: 200,
            TempFileSizeBytes: 1024 * 1024,
            PeakManagedMemoryBytes: 50L * 1024 * 1024);

        // Then
        Assert.Equal(150, metrics.SnapshotBuildMs);
        Assert.Equal(200, metrics.SnapshotUploadMs);
        Assert.Equal(1024 * 1024, metrics.TempFileSizeBytes);
        Assert.Equal(50L * 1024 * 1024, metrics.PeakManagedMemoryBytes);
    }

    [Fact]
    public void Should_Support_Value_Equality()
    {
        // Given
        var metrics1 = new SnapshotPersistMetrics(100, 200, 300, 400);
        var metrics2 = new SnapshotPersistMetrics(100, 200, 300, 400);

        // Then
        Assert.Equal(metrics1, metrics2);
    }

    [Fact]
    public void Should_Not_Equal_When_Values_Differ()
    {
        // Given
        var metrics1 = new SnapshotPersistMetrics(100, 200, 300, 400);
        var metrics2 = new SnapshotPersistMetrics(100, 200, 300, 500);

        // Then
        Assert.NotEqual(metrics1, metrics2);
    }

    [Fact]
    public void Should_Support_Zero_Values()
    {
        // When
        var metrics = new SnapshotPersistMetrics(0, 0, 0, 0);

        // Then
        Assert.Equal(0, metrics.SnapshotBuildMs);
        Assert.Equal(0, metrics.SnapshotUploadMs);
        Assert.Equal(0, metrics.TempFileSizeBytes);
        Assert.Equal(0, metrics.PeakManagedMemoryBytes);
    }
}
