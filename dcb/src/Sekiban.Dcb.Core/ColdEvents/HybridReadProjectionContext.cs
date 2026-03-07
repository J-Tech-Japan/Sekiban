using System.Threading;

namespace Sekiban.Dcb.ColdEvents;

/// <summary>
///     Ambient projection context for hybrid read diagnostics.
/// </summary>
public static class HybridReadProjectionContext
{
    private static readonly AsyncLocal<string?> CurrentProjectionName = new();

    public static string? ProjectionName => CurrentProjectionName.Value;

    public static IDisposable Push(string? projectionName)
    {
        var previous = CurrentProjectionName.Value;
        CurrentProjectionName.Value = projectionName;
        return new RestoreScope(previous);
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly string? _previous;
        private bool _disposed;

        public RestoreScope(string? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CurrentProjectionName.Value = _previous;
            _disposed = true;
        }
    }
}
