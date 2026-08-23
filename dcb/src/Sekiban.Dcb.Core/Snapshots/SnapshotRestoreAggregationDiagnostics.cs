namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Internal observability for the supported streaming restore seam. It deliberately records only calls made while
///     that seam is active, so an explicit compatibility fallback elsewhere does not make a supported restore look
///     buffered.
/// </summary>
internal static class SnapshotRestoreAggregationDiagnostics
{
    private static readonly AsyncLocal<Scope?> CurrentScope = new();

    internal static Scope BeginStreamingRestoreScope()
    {
        var scope = new Scope(CurrentScope.Value);
        CurrentScope.Value = scope;
        return scope;
    }

    /// <summary>
    ///     Called by the production whole-payload reader. If it is reached while a stream-capable restore is active,
    ///     the actor records it as a contract violation for that attempt.
    /// </summary>
    internal static void RecordWholePayloadAggregation() => CurrentScope.Value?.RecordWholePayloadAggregation();

    internal sealed class Scope(Scope? previous) : IDisposable
    {
        private bool _disposed;

        internal int WholePayloadAggregationCount { get; private set; }

        internal void RecordWholePayloadAggregation() => WholePayloadAggregationCount++;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentScope.Value = previous;
        }
    }
}
