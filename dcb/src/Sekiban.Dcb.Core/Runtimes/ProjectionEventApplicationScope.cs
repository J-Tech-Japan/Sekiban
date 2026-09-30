namespace Sekiban.Dcb.Runtime;

/// <summary>
///     Internal native application receipt. Records distinct accepted events at the wrapper's dedup boundary,
///     without consuming/promoting state merely to measure a version delta. Receipts survive a later batch failure.
/// </summary>
internal sealed class ProjectionEventApplicationScope : IDisposable
{
    private static readonly AsyncLocal<ProjectionEventApplicationScope?> Current = new();
    private readonly ProjectionEventApplicationScope? _previous;
    private readonly Action _onApplied;

    private ProjectionEventApplicationScope(Action onApplied)
    {
        _previous = Current.Value;
        _onApplied = onApplied;
        Current.Value = this;
    }

    internal int AppliedCount { get; private set; }
    internal bool ObservedNativeWrapper { get; private set; }
    internal static ProjectionEventApplicationScope Begin(Action onApplied) => new(onApplied);

    internal static void ObserveNativeWrapper()
    {
        if (Current.Value is { } scope) scope.ObservedNativeWrapper = true;
    }

    internal static void RecordAppliedEvent()
    {
        if (Current.Value is not { } scope) return;
        scope.AppliedCount++;
        scope._onApplied();
    }

    public void Dispose() => Current.Value = _previous;
}
