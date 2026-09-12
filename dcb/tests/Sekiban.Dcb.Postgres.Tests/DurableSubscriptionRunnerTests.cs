using Dcb.Domain.Weather;
using Microsoft.Extensions.Logging.Abstractions;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Subscriptions;
using Xunit;

namespace Sekiban.Dcb.Postgres.Tests;

/// <summary>Unit-level runner proofs use an in-memory event source only for deterministic lifecycle sequencing.</summary>
public sealed class DurableSubscriptionRunnerTests
{
    [Fact]
    public async Task RunnerAttachesNudgeBeforeReadingAndAcknowledgesOnlyAfterHandler()
    {
        var domain = global::Dcb.Domain.DomainType.GetDomainTypes();
        var source = new InMemoryEventStore(domain.EventTypes, new FixedServiceIdProvider("runner-service"));
        var eventId = SortableUniqueId.GenerateNew();
        var serializable = new Event(
                new WeatherForecastCreated(Guid.CreateVersion7(), "runner", DateOnly.FromDateTime(DateTime.UtcNow), 20, "unit"),
                eventId,
                nameof(WeatherForecastCreated),
                Guid.CreateVersion7(),
                new EventMetadata("runner", "runner", "test"),
                [])
            .ToSerializableEvent(domain.EventTypes);
        var write = await source.WriteSerializableEventsAsync([serializable]);
        Assert.True(write.IsSuccess, write.IsSuccess ? string.Empty : write.GetException().ToString());

        var nudge = new RecordingNudgeFactory();
        var stateStore = new ScriptedDurableSubscriptionStore("runner-service", "runner", nudge);
        var handled = new TaskCompletionSource<Event>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = new DurableSubscriptionRegistration(
            new DurableSubscriptionOptions
            {
                ServiceId = "runner-service",
                Name = "runner",
                StartPolicy = DurableSubscriptionStartPolicy.FromBeginning,
                PollInterval = TimeSpan.FromMilliseconds(10),
                RetryDelay = TimeSpan.FromMilliseconds(10),
                LeaseDuration = TimeSpan.FromSeconds(1)
            },
            (eventRecord, _) =>
            {
                handled.TrySetResult(eventRecord);
                return Task.CompletedTask;
            });
        var runner = new DurableSubscriptionRunner(
            registration,
            stateStore,
            new InMemoryEventStoreFactory(source),
            domain.EventTypes,
            nudge,
            NullLogger<DurableSubscriptionRunner>.Instance);

        using var cancellation = new CancellationTokenSource();
        await runner.StartAsync(cancellation.Token);
        var received = await handled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(eventId, received.SortableUniqueIdValue);
        Assert.True(nudge.CreatedBeforeFirstRead);
        await SpinWaitAsync(() => stateStore.AcknowledgementCount == 1 || stateStore.AcknowledgementError is not null);
        Assert.Null(stateStore.AcknowledgementError);
        Assert.Equal(1, stateStore.AcknowledgementCount);
        Assert.Equal(eventId, stateStore.State.AcknowledgedPosition);

        // A notification is data-free: waking an idle runner causes another authoritative read but cannot invoke the
        // handler with the notification payload or acknowledge a second event.
        var readsBeforeNudge = stateStore.ReadCount;
        await nudge.TriggerAsync();
        await SpinWaitAsync(() => stateStore.ReadCount > readsBeforeNudge);
        Assert.Equal(1, stateStore.AcknowledgementCount);

        cancellation.Cancel();
        await runner.StopAsync(CancellationToken.None);
    }

    private static async Task SpinWaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The durable runner did not observe the data-free nudge.");
    }

    private sealed class RecordingNudgeFactory : IDurableSubscriptionNudgeFactory
    {
        private Func<ValueTask>? _onNudge;
        internal bool CreatedBeforeFirstRead { get; private set; }
        internal bool FirstReadObserved { get; set; }

        public IDurableSubscriptionNudge Create(DurableSubscriptionIdentity identity, Func<ValueTask> onNudge)
        {
            _onNudge = onNudge;
            CreatedBeforeFirstRead = !FirstReadObserved;
            return new RecordingNudge();
        }

        internal ValueTask TriggerAsync() => _onNudge is null ? ValueTask.CompletedTask : _onNudge();
    }

    private sealed class RecordingNudge : IDurableSubscriptionNudge
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ScriptedDurableSubscriptionStore : IDurableSubscriptionStore
    {
        private readonly DurableSubscriptionIdentity _identity;
        private readonly RecordingNudgeFactory _nudge;
        private string? _owner;
        private long _generation;

        internal ScriptedDurableSubscriptionStore(string serviceId, string name, RecordingNudgeFactory nudge)
        {
            _nudge = nudge;
            _identity = new DurableSubscriptionIdentity(serviceId, name);
            State = new DurableSubscriptionState(
                _identity,
                Initialized: false,
                AcknowledgedPosition: null,
                DurableSubscriptionPhase.Uninitialized,
                null,
                0,
                null,
                null,
                null,
                null,
                null,
                null,
                0,
                null,
                DateTimeOffset.UtcNow);
        }

        internal DurableSubscriptionState State { get; private set; }
        internal int ReadCount { get; private set; }
        internal int AcknowledgementCount { get; private set; }
        internal Exception? AcknowledgementError { get; private set; }

        public Task<ResultBox<DurableSubscriptionState>> InitializeOrGetAsync(
            DurableSubscriptionIdentity identity,
            DurableSubscriptionStartPolicy startPolicy,
            string safeTail,
            CancellationToken cancellationToken = default)
        {
            State = State with
            {
                Initialized = true,
                AcknowledgedPosition = startPolicy == DurableSubscriptionStartPolicy.FromBeginning ? null : safeTail,
                Phase = DurableSubscriptionPhase.CatchingUp,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            return Task.FromResult(ResultBox.FromValue(State));
        }

        public Task<ResultBox<DurableSubscriptionState>> ReadAsync(
            DurableSubscriptionIdentity identity,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            _nudge.FirstReadObserved = true;
            return Task.FromResult(ResultBox.FromValue(State));
        }

        public Task<ResultBox<DurableSubscriptionLease>> TryAcquireAsync(
            DurableSubscriptionIdentity identity,
            string ownerId,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default)
        {
            _owner = ownerId;
            _generation++;
            State = State with
            {
                OwnerId = _owner,
                OwnerGeneration = _generation,
                LeaseExpiresAtUtc = DateTimeOffset.UtcNow.Add(leaseDuration),
                Phase = DurableSubscriptionPhase.CatchingUp,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            return Task.FromResult(ResultBox.FromValue(new DurableSubscriptionLease(true, State)));
        }

        public Task<ResultBox<bool>> RenewAsync(
            DurableSubscriptionIdentity identity,
            string ownerId,
            long ownerGeneration,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResultBox.FromValue(
                string.Equals(ownerId, _owner, StringComparison.Ordinal) && ownerGeneration == _generation));

        public Task<ResultBox<bool>> IsRetryDueAsync(
            DurableSubscriptionIdentity identity,
            string ownerId,
            long ownerGeneration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResultBox.FromValue(true));

        public Task<ResultBox<DurableSubscriptionState>> AcknowledgeAsync(
            DurableSubscriptionIdentity identity,
            string ownerId,
            long ownerGeneration,
            string position,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Assert.Equal(_owner, ownerId);
                Assert.Equal(_generation, ownerGeneration);
                AcknowledgementCount++;
                State = State with { AcknowledgedPosition = position, UpdatedAtUtc = DateTimeOffset.UtcNow };
                return Task.FromResult(ResultBox.FromValue(State));
            }
            catch (Exception ex)
            {
                AcknowledgementError = ex;
                throw;
            }
        }

        public Task<ResultBox<DurableSubscriptionState>> RecordFailureAsync(
            DurableSubscriptionIdentity identity,
            string ownerId,
            long ownerGeneration,
            string position,
            string reason,
            bool conversionFailure,
            int maxHandlerAttempts,
            TimeSpan retryDelay,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResultBox.FromValue(State));

        public Task<ResultBox<DurableSubscriptionState>> ResumeAsync(
            DurableSubscriptionIdentity identity,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResultBox.FromValue(State));

        public Task<ResultBox<DurableSubscriptionState>> HaltAsync(
            DurableSubscriptionIdentity identity,
            string ownerId,
            long ownerGeneration,
            string reason,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResultBox.FromValue(State with { Phase = DurableSubscriptionPhase.Halted }));

        public Task<ResultBox<DurableSubscriptionState>> MarkIdleAsync(
            DurableSubscriptionIdentity identity,
            string ownerId,
            long ownerGeneration,
            CancellationToken cancellationToken = default)
        {
            State = State with { Phase = DurableSubscriptionPhase.Idle, UpdatedAtUtc = DateTimeOffset.UtcNow };
            return Task.FromResult(ResultBox.FromValue(State));
        }
    }
}
