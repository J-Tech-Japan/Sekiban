using System.Threading;

namespace Sekiban.Dcb.ColdEvents;

/// <summary>
///     Ambient projection context for hybrid read diagnostics.
/// </summary>
public static class HybridReadProjectionContext
{
    private static readonly AsyncLocal<string?> CurrentProjectionName = new();
    private static readonly AsyncLocal<BatchMetadataHolder?> CurrentBatchMetadata = new();

    public static string? ProjectionName => CurrentProjectionName.Value;
    public static HybridReadBatchMetadata? BatchMetadata => CurrentBatchMetadata.Value?.Metadata;

    public static IDisposable Push(string? projectionName)
    {
        var previous = CurrentProjectionName.Value;
        var previousBatchMetadata = CurrentBatchMetadata.Value;
        CurrentProjectionName.Value = projectionName;
        CurrentBatchMetadata.Value = new BatchMetadataHolder();
        return new RestoreScope(previous, previousBatchMetadata);
    }

    internal static void SetBatchMetadata(HybridReadBatchMetadata? metadata)
    {
        if (CurrentBatchMetadata.Value is { } holder)
        {
            holder.Metadata = metadata;
        }
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly string? _previous;
        private readonly BatchMetadataHolder? _previousBatchMetadata;
        private bool _disposed;

        public RestoreScope(string? previous, BatchMetadataHolder? previousBatchMetadata)
        {
            _previous = previous;
            _previousBatchMetadata = previousBatchMetadata;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CurrentProjectionName.Value = _previous;
            CurrentBatchMetadata.Value = _previousBatchMetadata;
            _disposed = true;
        }
    }

    private sealed class BatchMetadataHolder
    {
        public HybridReadBatchMetadata? Metadata { get; set; }
    }
}

public sealed record HybridReadBatchMetadata(
    string Source,
    bool UsedCold,
    bool UsedHot,
    bool ReachedColdSegmentBoundary,
    int ColdEventsRead,
    int HotEventsRead,
    int SegmentCount);
