using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.Subscriptions;

/// <summary>
///     Store-driven durable subscriber. Orleans (or another delivery mechanism) only releases the wake gate;
///     every event and acknowledgement comes from the authoritative event store and durable state store.
/// </summary>
public sealed class DurableSubscriptionRunner : BackgroundService
{
    private readonly DurableSubscriptionRegistration _registration;
    private readonly IDurableSubscriptionStore _stateStore;
    private readonly IEventStoreFactory _eventStoreFactory;
    private readonly IEventTypes _eventTypes;
    private readonly IDurableSubscriptionNudgeFactory _nudgeFactory;
    private readonly ILogger<DurableSubscriptionRunner> _logger;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly string _ownerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private IDurableSubscriptionNudge? _nudge;
    private int _status = (int)DurableSubscriptionRunnerStatus.Starting;
    private long _ownerGeneration;

    public DurableSubscriptionRunner(
        DurableSubscriptionRegistration registration,
        IDurableSubscriptionStore stateStore,
        IEventStoreFactory eventStoreFactory,
        IEventTypes eventTypes,
        IDurableSubscriptionNudgeFactory nudgeFactory,
        ILogger<DurableSubscriptionRunner> logger)
    {
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _eventStoreFactory = eventStoreFactory ?? throw new ArgumentNullException(nameof(eventStoreFactory));
        _eventTypes = eventTypes ?? throw new ArgumentNullException(nameof(eventTypes));
        _nudgeFactory = nudgeFactory ?? throw new ArgumentNullException(nameof(nudgeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _registration.Options.Validate();
    }

    public DurableSubscriptionIdentity Identity => _registration.Options.Identity;

    public DurableSubscriptionRunnerStatus Status =>
        (DurableSubscriptionRunnerStatus)Volatile.Read(ref _status);

    public Task<DurableSubscriptionState> GetStateAsync(CancellationToken cancellationToken = default) =>
        GetStateOrThrowAsync(cancellationToken);

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _stateStore.ResumeAsync(Identity, cancellationToken).ConfigureAwait(false);

        Volatile.Write(ref _status, (int)DurableSubscriptionRunnerStatus.Starting);
        _wake.ReleaseIfNeeded();
    }

    public async Task HaltAsync(string reason, CancellationToken cancellationToken = default)
    {
        var generation = Interlocked.Read(ref _ownerGeneration);
        if (generation == 0)
        {
            throw new InvalidOperationException("The durable subscription runner does not currently own a lease.");
        }

        await _stateStore.HaltAsync(
                Identity,
                _ownerId,
                generation,
                reason,
                cancellationToken)
            .ConfigureAwait(false);

        Volatile.Write(ref _status, (int)DurableSubscriptionRunnerStatus.Halted);
        _wake.ReleaseIfNeeded();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _registration.Options.Validate();
        var identity = Identity;
        var safeTail = DurableSubscriptionSafeTail.Calculate(_registration.Options.SafeWindow);
        // Store failures are logged and handled here, as the runner always did; they never stop the host.
        var pendingInitializeOrGet = _stateStore.InitializeOrGetAsync(
            identity,
            _registration.Options.StartPolicy,
            safeTail,
            stoppingToken);
        try
        {
            await pendingInitializeOrGet.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Durable subscription {ServiceId}/{Name} could not initialize.", identity.ServiceId, identity.Name);
            Volatile.Write(ref _status, (int)DurableSubscriptionRunnerStatus.Stopped);
            return;
        }

        // The nudge is attached before the first catch-up read. It is deliberately data-free and can never advance the
        // durable cursor; this ordering closes the startup notification gap without trusting stream payloads.
        try
        {
            _nudge = _nudgeFactory.Create(identity, () =>
            {
                _wake.ReleaseIfNeeded();
                return ValueTask.CompletedTask;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Durable subscription {ServiceId}/{Name} nudge attachment failed; polling remains active.", identity.ServiceId, identity.Name);
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOneCycleAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (_nudge is not null)
            {
                await _nudge.DisposeAsync().ConfigureAwait(false);
            }

            Interlocked.Exchange(ref _ownerGeneration, 0);
            Volatile.Write(ref _status, (int)DurableSubscriptionRunnerStatus.Stopped);
        }
    }

    private async Task RunOneCycleAsync(CancellationToken cancellationToken)
    {
        var identity = Identity;
        DurableSubscriptionState state;
        var pendingRead = _stateStore.ReadAsync(identity, cancellationToken);
        try
        {
            state = await pendingRead.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Durable subscription {ServiceId}/{Name} state read failed.", identity.ServiceId, identity.Name);
            await WaitForWakeOrPollAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (state.Phase == DurableSubscriptionPhase.Halted)
        {
            Volatile.Write(ref _status, (int)DurableSubscriptionRunnerStatus.Halted);
            await WaitForWakeOrPollAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        // A row read immediately precedes each acquisition attempt. The provider repeats the predicate under its
        // transaction, but this read makes standby state observable and prevents an optimistic local lease assumption.
        DurableSubscriptionLease lease;
        var pendingTryAcquire = _stateStore.TryAcquireAsync(
            identity,
            _ownerId,
            _registration.Options.LeaseDuration,
            cancellationToken);
        try
        {
            lease = await pendingTryAcquire.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Durable subscription {ServiceId}/{Name} acquisition failed.", identity.ServiceId, identity.Name);
            await WaitForWakeOrPollAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!lease.Acquired)
        {
            Volatile.Write(
                ref _status,
                (int)(lease.Status == DurableSubscriptionRunnerStatus.Halted
                    ? DurableSubscriptionRunnerStatus.Halted
                    : DurableSubscriptionRunnerStatus.Standby));
            await WaitForWakeOrPollAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        state = lease.State;
        Interlocked.Exchange(ref _ownerGeneration, state.OwnerGeneration);
        Volatile.Write(ref _status, (int)DurableSubscriptionRunnerStatus.Owner);
        var store = _eventStoreFactory.CreateForService(identity.ServiceId);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var pendingCurrentRead = _stateStore.ReadAsync(identity, cancellationToken);
                try
                {
                    state = await pendingCurrentRead.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Durable subscription {ServiceId}/{Name} state read failed while owned.", identity.ServiceId, identity.Name);
                    return;
                }

                if (state.Phase == DurableSubscriptionPhase.Halted)
                {
                    Interlocked.Exchange(ref _ownerGeneration, 0);
                    Volatile.Write(ref _status, (int)DurableSubscriptionRunnerStatus.Halted);
                    return;
                }

                if (!await TryRenewAsync(_stateStore.RenewAsync(
                        identity,
                        _ownerId,
                        state.OwnerGeneration,
                        _registration.Options.LeaseDuration,
                        cancellationToken)).ConfigureAwait(false))
                {
                    Interlocked.Exchange(ref _ownerGeneration, 0);
                    Volatile.Write(ref _status, (int)DurableSubscriptionRunnerStatus.Standby);
                    return;
                }

                if (state.Phase == DurableSubscriptionPhase.Retrying)
                {
                    bool retryDue;
                    var pendingIsRetryDue = _stateStore.IsRetryDueAsync(
                        identity,
                        _ownerId,
                        state.OwnerGeneration,
                        cancellationToken);
                    try
                    {
                        retryDue = await pendingIsRetryDue.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Durable subscription {ServiceId}/{Name} retry timing could not be read.", identity.ServiceId, identity.Name);
                        return;
                    }

                    if (!retryDue)
                    {
                        await WaitForWakeOrPollAsync(cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                var read = store.ReadAllSerializableEventsAsync(
                    state.AcknowledgedPosition is null ? null : new SortableUniqueId(state.AcknowledgedPosition),
                    _registration.Options.MaxBatchSize);
                IEnumerable<SerializableEvent> readEvents;
                try
                {
                    readEvents = await read.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Durable subscription {ServiceId}/{Name} event read failed.", identity.ServiceId, identity.Name);
                    await WaitForWakeOrPollAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                var events = readEvents.OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal).ToList();
                if (events.Count == 0)
                {
                    var pendingMarkIdle = _stateStore.MarkIdleAsync(identity, _ownerId, state.OwnerGeneration, cancellationToken);
                    try
                    {
                        await pendingMarkIdle.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Durable subscription {ServiceId}/{Name} could not mark idle.", identity.ServiceId, identity.Name);
                    }

                    await WaitForWakeOrPollAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                foreach (var serializableEvent in events)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    Event converted;
                    try
                    {
                        converted = serializableEvent.ToEvent(_eventTypes);
                    }
                    catch (Exception conversionFailure)
                    {
                        var failed = await RecordFailureAndStopOrRetryAsync(
                                serializableEvent,
                                conversionFailure.Message,
                                conversionFailure: true,
                                state.OwnerGeneration,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (failed?.Phase == DurableSubscriptionPhase.Retrying)
                        {
                            return;
                        }

                        return;
                    }

                    var delivery = await InvokeWithLeaseRenewalAsync(
                            converted,
                            state.OwnerGeneration,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!delivery.Completed)
                    {
                        if (delivery.LeaseLost)
                        {
                            Interlocked.Exchange(ref _ownerGeneration, 0);
                            Volatile.Write(ref _status, (int)DurableSubscriptionRunnerStatus.Standby);
                            return;
                        }

                        var failed = await RecordFailureAndStopOrRetryAsync(
                                serializableEvent,
                                delivery.Error?.ToString() ?? "Durable subscription handler failed.",
                                conversionFailure: false,
                                state.OwnerGeneration,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (failed?.Phase == DurableSubscriptionPhase.Retrying)
                        {
                            return;
                        }

                        return;
                    }

                    var pendingAcknowledge = _stateStore.AcknowledgeAsync(
                        identity,
                        _ownerId,
                        state.OwnerGeneration,
                        serializableEvent.SortableUniqueIdValue,
                        cancellationToken);
                    try
                    {
                        await pendingAcknowledge.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Durable subscription {ServiceId}/{Name} acknowledgement failed.", identity.ServiceId, identity.Name);
                        return;
                    }
                }
            }
        }
        finally
        {
            if (store is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private async Task<DurableSubscriptionState?> RecordFailureAndStopOrRetryAsync(
        SerializableEvent serializableEvent,
        string reason,
        bool conversionFailure,
        long ownerGeneration,
        CancellationToken cancellationToken)
    {
        DurableSubscriptionState state;
        var pendingRecordFailure = _stateStore.RecordFailureAsync(
            Identity,
            _ownerId,
            ownerGeneration,
            serializableEvent.SortableUniqueIdValue,
            reason,
            conversionFailure,
            _registration.Options.MaxHandlerAttempts,
            _registration.Options.RetryDelay,
            cancellationToken);
        try
        {
            state = await pendingRecordFailure.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Durable subscription {ServiceId}/{Name} failure state could not be recorded.", Identity.ServiceId, Identity.Name);
            return null;
        }

        if (state.Phase == DurableSubscriptionPhase.Halted)
        {
            Interlocked.Exchange(ref _ownerGeneration, 0);
            Volatile.Write(ref _status, (int)DurableSubscriptionRunnerStatus.Halted);
            _logger.LogError("Durable subscription {ServiceId}/{Name} halted at {Position}: {Reason}", Identity.ServiceId, Identity.Name, state.LastHaltPosition, state.LastHaltReason);
        }

        return state;
    }

    private async Task<(bool Completed, bool LeaseLost, Exception? Error)> InvokeWithLeaseRenewalAsync(
        Event @event,
        long ownerGeneration,
        CancellationToken cancellationToken)
    {
        using var handlerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var leaseLost = 0;
        var renewTask = RenewLeaseAsync(ownerGeneration, handlerCts, () => Interlocked.Exchange(ref leaseLost, 1));
        try
        {
            await _registration.Handler(@event, handlerCts.Token).ConfigureAwait(false);
            return (true, false, null);
        }
        catch (OperationCanceledException) when (Volatile.Read(ref leaseLost) != 0 && !cancellationToken.IsCancellationRequested)
        {
            return (false, true, null);
        }
        catch (Exception ex)
        {
            return (false, false, ex);
        }
        finally
        {
            handlerCts.Cancel();
            try
            {
                await renewTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task RenewLeaseAsync(
        long ownerGeneration,
        CancellationTokenSource handlerCts,
        Action markLeaseLost)
    {
        var delay = TimeSpan.FromTicks(Math.Max(TimeSpan.FromMilliseconds(100).Ticks, _registration.Options.LeaseDuration.Ticks / 3));
        while (!handlerCts.IsCancellationRequested)
        {
            await Task.Delay(delay, handlerCts.Token).ConfigureAwait(false);
            if (handlerCts.IsCancellationRequested)
            {
                break;
            }

            try
            {
                if (!await TryRenewAsync(_stateStore.RenewAsync(
                        Identity,
                        _ownerId,
                        ownerGeneration,
                        _registration.Options.LeaseDuration,
                        handlerCts.Token)).ConfigureAwait(false))
                {
                    markLeaseLost();
                    handlerCts.Cancel();
                    break;
                }
            }
            catch (OperationCanceledException) when (handlerCts.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                markLeaseLost();
                handlerCts.Cancel();
                break;
            }
        }
    }

    private Task<DurableSubscriptionState> GetStateOrThrowAsync(CancellationToken cancellationToken) =>
        _stateStore.ReadAsync(Identity, cancellationToken);

    // A renewal the store reports as failed, or whose task fails, means the lease is no longer held. The renewal task is
    // created by the caller, so a store that throws before returning it propagates there as it always did.
    private static async Task<bool> TryRenewAsync(Task<bool> renewal)
    {
        try
        {
            return await renewal.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task WaitForWakeOrPollAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _wake.WaitAsync(_registration.Options.PollInterval, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

}

internal static class DurableSubscriptionSafeTail
{
    internal static string Calculate(TimeSpan safeWindow)
    {
        var timestamp = DateTime.UtcNow.Subtract(safeWindow);
        return SortableUniqueId.Generate(timestamp, Guid.Empty);
    }
}

public sealed class NoOpDurableSubscriptionNudgeFactory : IDurableSubscriptionNudgeFactory
{
    public IDurableSubscriptionNudge Create(DurableSubscriptionIdentity identity, Func<ValueTask> onNudge) =>
        new NoOpDurableSubscriptionNudge();
}

internal sealed class NoOpDurableSubscriptionNudge : IDurableSubscriptionNudge
{
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class DurableSubscriptionHandle : IDurableSubscriptionHandle
{
    private readonly DurableSubscriptionRunner _runner;

    public DurableSubscriptionHandle(DurableSubscriptionRunner runner) => _runner = runner;

    public DurableSubscriptionIdentity Identity => _runner.Identity;
    public DurableSubscriptionRunnerStatus Status => _runner.Status;
    public Task<DurableSubscriptionState> GetStateAsync(CancellationToken cancellationToken = default) => _runner.GetStateAsync(cancellationToken);
    public Task ResumeAsync(CancellationToken cancellationToken = default) => _runner.ResumeAsync(cancellationToken);
    public Task HaltAsync(string reason, CancellationToken cancellationToken = default) => _runner.HaltAsync(reason, cancellationToken);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static class SemaphoreSlimExtensions
{
    internal static void ReleaseIfNeeded(this SemaphoreSlim semaphore)
    {
        try
        {
            semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }
}
