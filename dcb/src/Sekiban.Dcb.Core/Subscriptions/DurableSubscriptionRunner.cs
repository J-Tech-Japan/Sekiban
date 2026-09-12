using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ResultBoxes;
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

    public Task<DurableSubscriptionState> GetStateAsync(CancellationToken cancellationToken = default) =>
        GetStateOrThrowAsync(cancellationToken);

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        var result = await _stateStore.ResumeAsync(Identity, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw result.GetException();
        }

        _wake.ReleaseIfNeeded();
    }

    public async Task HaltAsync(string reason, CancellationToken cancellationToken = default)
    {
        var result = await _stateStore.HaltAsync(Identity, reason, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw result.GetException();
        }

        _wake.ReleaseIfNeeded();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _registration.Options.Validate();
        var identity = Identity;
        var safeTail = DurableSubscriptionSafeTail.Calculate(_registration.Options.SafeWindow);
        var initialized = await _stateStore.InitializeOrGetAsync(
                identity,
                _registration.Options.StartPolicy,
                safeTail,
                stoppingToken)
            .ConfigureAwait(false);
        if (!initialized.IsSuccess)
        {
            _logger.LogError(initialized.GetException(), "Durable subscription {ServiceId}/{Name} could not initialize.", identity.ServiceId, identity.Name);
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
        }
    }

    private async Task RunOneCycleAsync(CancellationToken cancellationToken)
    {
        var identity = Identity;
        var observed = await _stateStore.ReadAsync(identity, cancellationToken).ConfigureAwait(false);
        if (!observed.IsSuccess)
        {
            _logger.LogError(observed.GetException(), "Durable subscription {ServiceId}/{Name} state read failed.", identity.ServiceId, identity.Name);
            await WaitForWakeOrPollAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var state = observed.GetValue();
        if (state.Phase == DurableSubscriptionPhase.Halted)
        {
            await WaitForWakeOrPollAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        // A row read immediately precedes each acquisition attempt. The provider repeats the predicate under its
        // transaction, but this read makes standby state observable and prevents an optimistic local lease assumption.
        var lease = await _stateStore.TryAcquireAsync(
                identity,
                _ownerId,
                _registration.Options.LeaseDuration,
                cancellationToken)
            .ConfigureAwait(false);
        if (!lease.IsSuccess)
        {
            _logger.LogError(lease.GetException(), "Durable subscription {ServiceId}/{Name} acquisition failed.", identity.ServiceId, identity.Name);
            await WaitForWakeOrPollAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!lease.GetValue().Acquired)
        {
            await WaitForWakeOrPollAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        state = lease.GetValue().State;
        var store = _eventStoreFactory.CreateForService(identity.ServiceId);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var currentResult = await _stateStore.ReadAsync(identity, cancellationToken).ConfigureAwait(false);
                if (!currentResult.IsSuccess)
                {
                    _logger.LogError(currentResult.GetException(), "Durable subscription {ServiceId}/{Name} state read failed while owned.", identity.ServiceId, identity.Name);
                    return;
                }

                state = currentResult.GetValue();
                if (!Owns(state) || state.Phase == DurableSubscriptionPhase.Halted)
                {
                    return;
                }

                if (state.Phase == DurableSubscriptionPhase.Retrying &&
                    state.NextAttemptAtUtc is { } retryAt && retryAt > DateTimeOffset.UtcNow)
                {
                    await DelayUntilAsync(retryAt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var read = await store.ReadAllSerializableEventsAsync(
                        state.AcknowledgedPosition is null ? null : new SortableUniqueId(state.AcknowledgedPosition),
                        _registration.Options.MaxBatchSize)
                    .ConfigureAwait(false);
                if (!read.IsSuccess)
                {
                    _logger.LogError(read.GetException(), "Durable subscription {ServiceId}/{Name} event read failed.", identity.ServiceId, identity.Name);
                    await WaitForWakeOrPollAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                var events = read.GetValue().OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal).ToList();
                if (events.Count == 0)
                {
                    var idle = await _stateStore.MarkIdleAsync(identity, _ownerId, state.OwnerGeneration, cancellationToken).ConfigureAwait(false);
                    if (!idle.IsSuccess)
                    {
                        _logger.LogWarning(idle.GetException(), "Durable subscription {ServiceId}/{Name} could not mark idle.", identity.ServiceId, identity.Name);
                    }

                    await WaitForWakeOrPollAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                foreach (var serializableEvent in events)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    var conversion = serializableEvent.ToEvent(_eventTypes);
                    if (!conversion.IsSuccess)
                    {
                        var failed = await RecordFailureAndStopOrRetryAsync(
                                serializableEvent,
                                conversion.GetException().Message,
                                conversionFailure: true,
                                state.OwnerGeneration,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (failed?.Phase == DurableSubscriptionPhase.Retrying)
                        {
                            await DelayUntilAsync(failed.NextAttemptAtUtc ?? DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        return;
                    }

                    var delivery = await InvokeWithLeaseRenewalAsync(
                            conversion.GetValue(),
                            state.OwnerGeneration,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!delivery.Completed)
                    {
                        if (delivery.LeaseLost)
                        {
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
                            await DelayUntilAsync(failed.NextAttemptAtUtc ?? DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        return;
                    }

                    var acknowledged = await _stateStore.AcknowledgeAsync(
                            identity,
                            _ownerId,
                            state.OwnerGeneration,
                            serializableEvent.SortableUniqueIdValue,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!acknowledged.IsSuccess)
                    {
                        _logger.LogError(acknowledged.GetException(), "Durable subscription {ServiceId}/{Name} acknowledgement failed.", identity.ServiceId, identity.Name);
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
        var result = await _stateStore.RecordFailureAsync(
                Identity,
                _ownerId,
                ownerGeneration,
                serializableEvent.SortableUniqueIdValue,
                reason,
                conversionFailure,
                _registration.Options.MaxHandlerAttempts,
                _registration.Options.RetryDelay,
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            _logger.LogError(result.GetException(), "Durable subscription {ServiceId}/{Name} failure state could not be recorded.", Identity.ServiceId, Identity.Name);
            return null;
        }

        var state = result.GetValue();
        if (state.Phase == DurableSubscriptionPhase.Halted)
        {
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
                var renewed = await _stateStore.RenewAsync(
                        Identity,
                        _ownerId,
                        ownerGeneration,
                        _registration.Options.LeaseDuration,
                        handlerCts.Token)
                    .ConfigureAwait(false);
                if (!renewed.IsSuccess || !renewed.GetValue())
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

    private async Task<DurableSubscriptionState> GetStateOrThrowAsync(CancellationToken cancellationToken)
    {
        var result = await _stateStore.ReadAsync(Identity, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? result.GetValue() : throw result.GetException();
    }

    private bool Owns(DurableSubscriptionState state) =>
        string.Equals(state.OwnerId, _ownerId, StringComparison.Ordinal) &&
        state.LeaseExpiresAtUtc is { } expiry && expiry > DateTimeOffset.UtcNow;

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

    private static async Task DelayUntilAsync(DateTimeOffset target, CancellationToken cancellationToken)
    {
        var delay = target - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
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
