using System.Threading;

namespace Sekiban.Dcb.ColdEvents;

/// <summary>
///     Ambient projection context for hybrid read diagnostics.
/// </summary>
public static class HybridReadProjectionContext
{
    private static readonly AsyncLocal<string?> CurrentProjectionName = new();
    private static readonly AsyncLocal<HybridReadBatchMetadata?> CurrentBatchMetadata = new();

    public static string? ProjectionName => CurrentProjectionName.Value;
    public static HybridReadBatchMetadata? BatchMetadata => CurrentBatchMetadata.Value;

    public static IDisposable Push(string? projectionName)
    {
        var previous = CurrentProjectionName.Value;
        var previousBatchMetadata = CurrentBatchMetadata.Value;
        CurrentProjectionName.Value = projectionName;
        CurrentBatchMetadata.Value = null;
        return new RestoreScope(previous, previousBatchMetadata);
    }

    internal static void SetBatchMetadata(HybridReadBatchMetadata? metadata)
        => CurrentBatchMetadata.Value = metadata;

    private sealed class RestoreScope : IDisposable
    {
        private readonly string? _previous;
        private readonly HybridReadBatchMetadata? _previousBatchMetadata;
        private bool _disposed;

        public RestoreScope(string? previous, HybridReadBatchMetadata? previousBatchMetadata)
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
}

public sealed record HybridReadBatchMetadata(
    string Source,
    bool UsedCold,
    bool UsedHot,
    bool ReachedColdSegmentBoundary,
    int ColdEventsRead,
    int HotEventsRead,
    int SegmentCount);
