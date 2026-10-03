namespace Sekiban.Dcb.MaterializedView;

/// <summary>
///     Internal deterministic test seams for provider lifecycle lock ordering. They are scoped through AsyncLocal so
///     parallel provider fixtures cannot observe one another's barriers and no public runtime surface is added.
/// </summary>
internal static class MvLifecycleTestHooks
{
    private static readonly AsyncLocal<Func<System.Data.Common.DbConnection, CancellationToken, Task>?> BeforeApplyTransaction = new();
    private static readonly AsyncLocal<Func<System.Data.Common.DbConnection, CancellationToken, Task>?> BeforeApplyLockRead = new();
    private static readonly AsyncLocal<Func<CancellationToken, Task<IReadOnlyList<MvRegistryEntry>>>?> LockReadOverride = new();

    private static readonly AsyncLocal<bool> DisableApplyLock = new();
    internal static bool ApplyLockDisabled => DisableApplyLock.Value;
    internal static IDisposable PushApplyLockDisabled()
    {
        var previous = DisableApplyLock.Value;
        DisableApplyLock.Value = true;
        return new CallbackScope(() => DisableApplyLock.Value = previous);
    }

    internal static Func<CancellationToken, Task<IReadOnlyList<MvRegistryEntry>>>? ApplyLockReadOverride => LockReadOverride.Value;

    internal static IDisposable PushBeforeApplyTransaction(Func<System.Data.Common.DbConnection, CancellationToken, Task> hook) =>
        Push(BeforeApplyTransaction, hook);
    internal static IDisposable PushBeforeApplyLockRead(Func<System.Data.Common.DbConnection, CancellationToken, Task> hook) =>
        Push(BeforeApplyLockRead, hook);
    internal static IDisposable PushApplyLockReadOverride(Func<CancellationToken, Task<IReadOnlyList<MvRegistryEntry>>> hook) =>
        Push(LockReadOverride, hook);

    internal static Task InvokeBeforeApplyTransactionAsync(System.Data.Common.DbConnection connection, CancellationToken ct) =>
        BeforeApplyTransaction.Value?.Invoke(connection, ct) ?? Task.CompletedTask;
    internal static Task InvokeBeforeApplyLockReadAsync(System.Data.Common.DbConnection connection, CancellationToken ct) =>
        BeforeApplyLockRead.Value?.Invoke(connection, ct) ?? Task.CompletedTask;

    private static IDisposable Push<T>(AsyncLocal<T?> slot, T value) where T : class
    {
        var previous = slot.Value;
        slot.Value = value;
        return new CallbackScope(() => slot.Value = previous);
    }
    private sealed class CallbackScope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

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
    ForcedReverseRegistry = 1,
    ApplyRegistry = 2
}
