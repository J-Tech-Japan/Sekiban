namespace Sekiban.Dcb.MaterializedView;

/// <summary>
///     Internal deterministic test seams for provider lifecycle lock ordering. They are scoped through AsyncLocal so
///     parallel provider fixtures cannot observe one another's barriers and no public runtime surface is added.
/// </summary>
internal static class MvLifecycleTestHooks
{
    private static readonly AsyncLocal<Func<MvLifecycleLockPoint, CancellationToken, Task>?> AfterRegistryLock = new();

    internal static IDisposable PushAfterRegistryLock(
        Func<MvLifecycleLockPoint, CancellationToken, Task> barrier)
    {
        ArgumentNullException.ThrowIfNull(barrier);
        var previous = AfterRegistryLock.Value;
        AfterRegistryLock.Value = barrier;
        return new Scope(previous);
    }

    internal static Task InvokeAfterRegistryLockAsync(
        MvLifecycleLockPoint point,
        CancellationToken cancellationToken) =>
        AfterRegistryLock.Value is { } barrier
            ? barrier(point, cancellationToken)
            : Task.CompletedTask;

    private sealed class Scope(Func<MvLifecycleLockPoint, CancellationToken, Task>? previous) : IDisposable
    {
        public void Dispose() => AfterRegistryLock.Value = previous;
    }
}

internal enum MvLifecycleLockPoint
{
    ActivationRegistry = 0,
    ForcedReverseRegistry = 1
}
