using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Storage.Checkpoints;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     SEK-G90 / #1253: deterministic parked timer tests on a non-reentrant Orleans grain.
/// </summary>
public class FirstQueryBoundedWaitClusterTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private IClusterClient _client => _cluster.Client;

    public async Task InitializeAsync()
    {
        Env.Reset();
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"G90-bw-{uniqueId}";
        builder.Options.ServiceId = $"G90-bw-{uniqueId}";
        builder.AddSiloBuilderConfigurator<Configurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }

    private IDisposable ParkBackground(TaskCompletionSource release, Action<CatchUpProductionObservation>? entered = null) =>
        CatchUpProductionTestHooks.Register(Sekiban.Dcb.ServiceId.DefaultServiceIdProvider.DefaultServiceId,
            CountProjector.MultiProjectorName, async (point, observation) =>
            {
                if (point == CatchUpProductionHookPoint.BackgroundEnteredGate)
                {
                    entered?.Invoke(observation);
                    await release.Task;
                }
            });

    private async Task<IMultiProjectionGrain> FreshAsync()
    {
        await Env.EventStore.WriteSerializableEventsAsync(Enumerable.Range(0, 3)
            .Select(i => ToSerializable(CreateEvent(new Counted($"e{i}"), DateTime.UtcNow.AddSeconds(-60 + i)))).ToArray());
        return _client.GetGrain<IMultiProjectionGrain>(CountProjector.MultiProjectorName);
    }

    [Theory]
    [InlineData("state")]
    [InlineData("snapshot")]
    [InlineData("scalar")]
    [InlineData("list")]
    public async Task ParkedBackground_AllSurfacesFailWithinBudget_WithoutInvocationReplay(string surface)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocations = 0;
        var leases = new HashSet<CatchUpStartPositionLease>(ReferenceEqualityComparer.Instance);
        using var hook = CatchUpProductionTestHooks.Register(
            Sekiban.Dcb.ServiceId.DefaultServiceIdProvider.DefaultServiceId, CountProjector.MultiProjectorName,
            async (point, observation) =>
            {
                if (point == CatchUpProductionHookPoint.InvocationEnteredGate) Interlocked.Increment(ref invocations);
                if (point == CatchUpProductionHookPoint.BackgroundEnteredGate)
                {
                    lock (leases) leases.Add(observation.Start!);
                    await release.Task;
                }
            });
        try
        {
            var grain = await FreshAsync();
            var executor = new OrleansDcbExecutor(_client, Env.EventStore, Env.Domain);
            var sw = Stopwatch.StartNew();
            Exception error;
            if (surface == "state") error = (await grain.GetStateAsync()).GetException();
            else if (surface == "snapshot") error = (await grain.GetSnapshotJsonAsync()).GetException();
            else
            {
                try
                {
                    error = surface == "scalar"
                        ? (await executor.QueryAsync(new CountQuery())).GetException()
                        : (await executor.QueryAsync(new CountListQuery())).GetException();
                }
                catch (InvalidOperationException ex) { error = ex; }
            }
            Assert.StartsWith(MultiProjectionQueryFailClosedMessages.CatchUpInProgressPrefix, error.Message);
            Assert.Contains("current position", error.Message);
            Assert.Contains("target position", error.Message);
            Assert.Contains("events processed", error.Message);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), sw.Elapsed.ToString());
            Assert.Equal(0, invocations);
            var status = await grain.GetStatusAsync();
            Assert.True(status.FirstQueryCatchUpPending);
            Assert.Null(status.LastError);
            var polls = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => grain.GetStateAsync()));
            Assert.All(polls, result => Assert.False(result.IsSuccess));
            lock (leases) Assert.Single(leases);
            release.TrySetResult();
            await PollUntilAsync(async () => !(await grain.GetStatusAsync()).FirstQueryCatchUpPending);
            Assert.Equal(3, ((CountProjector)(await grain.GetStateAsync()).GetValue().Payload).Count);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task ParkedIdleSettlement_PollsDoNotRestartCatchUp_AndLaterQuerySucceeds()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        var settlements = 0;
        using var hook = CatchUpProductionTestHooks.Register(
            Sekiban.Dcb.ServiceId.DefaultServiceIdProvider.DefaultServiceId, CountProjector.MultiProjectorName,
            async (point, _) =>
            {
                if (point == CatchUpProductionHookPoint.BackgroundStarted) Interlocked.Increment(ref starts);
                if (point == CatchUpProductionHookPoint.InvocationBeforeRead)
                {
                    Interlocked.Increment(ref settlements);
                    parked.TrySetResult();
                    await release.Task;
                }
            });
        try
        {
            var grain = await FreshAsync();
            _ = await grain.GetStateAsync();
            // The timer has completed; Ensure now owns the execution gate and active progress, with no timer.
            await parked.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(1, Volatile.Read(ref starts));
            var polls = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => grain.GetStateAsync()));
            Assert.All(polls, result =>
            {
                Assert.False(result.IsSuccess);
                Assert.StartsWith(MultiProjectionQueryFailClosedMessages.CatchUpInProgressPrefix,
                    result.GetException().Message);
            });
            Assert.Equal(1, Volatile.Read(ref starts));
            Assert.Equal(1, Volatile.Read(ref settlements));
            Assert.True((await grain.GetStatusAsync()).FirstQueryCatchUpPending);
            release.TrySetResult();
            await PollUntilAsync(async () => !(await grain.GetStatusAsync()).FirstQueryCatchUpPending);
            var result = await grain.GetStateAsync();
            Assert.True(result.IsSuccess);
            Assert.Equal(3, ((CountProjector)result.GetValue().Payload).Count);
            Assert.Equal(1, Volatile.Read(ref starts));
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task FastBackground_SucceedsWithinLargeBudget()
    {
        Env.WaitMs = 20_000;
        var grain = await FreshAsync();
        var result = await grain.GetStateAsync();
        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Equal(3, ((CountProjector)result.GetValue().Payload).Count);
        Assert.False((await grain.GetStatusAsync()).FirstQueryCatchUpPending);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task DefaultPath_RunsInvocationDespiteParkedBackground(int waitMs)
    {
        Env.WaitMs = waitMs;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var hook = CatchUpProductionTestHooks.Register(
            Sekiban.Dcb.ServiceId.DefaultServiceIdProvider.DefaultServiceId, CountProjector.MultiProjectorName,
            async (point, _) =>
            {
                if (point == CatchUpProductionHookPoint.BackgroundBeforeGate) await release.Task;
                if (point == CatchUpProductionHookPoint.InvocationEnteredGate) entered.TrySetResult();
            });
        try
        {
            var grain = await FreshAsync();
            var result = await grain.GetStateAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.IsSuccess);
            Assert.True(entered.Task.IsCompleted);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task TailPoison_FaultPrecedesCatchUpError_AndDoesNotStartNewRun()
    {
        var leases = new HashSet<CatchUpStartPositionLease>(ReferenceEqualityComparer.Instance);
        using var hook = CatchUpProductionTestHooks.Register(
            Sekiban.Dcb.ServiceId.DefaultServiceIdProvider.DefaultServiceId, CountProjector.MultiProjectorName,
            (point, observation) =>
            {
                if (point == CatchUpProductionHookPoint.BackgroundEnteredGate)
                    lock (leases) leases.Add(observation.Start!);
                return Task.CompletedTask;
            });
        var grain = await FreshAsync();
        await Env.EventStore.WriteSerializableEventsAsync([
            ToSerializable(CreateEvent(new Counted("poison"), DateTime.UtcNow.AddSeconds(-50)))]);
        await PollUntilAsync(async () =>
        {
            var result = await grain.GetStateAsync();
            return !result.IsSuccess && !result.GetException().Message.StartsWith(
                MultiProjectionQueryFailClosedMessages.CatchUpInProgressPrefix, StringComparison.Ordinal);
        });
        int runs;
        lock (leases) runs = leases.Count;
        for (var i = 0; i < 3; i++)
        {
            var result = await grain.GetStateAsync();
            Assert.False(result.IsSuccess);
            Assert.DoesNotContain(MultiProjectionQueryFailClosedMessages.CatchUpInProgressPrefix, result.GetException().Message);
            Assert.Contains("tail poison", result.GetException().Message);
        }
        lock (leases) Assert.Equal(runs, leases.Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailedAttempt_RestartsIncrementally_WithoutDoubleApplying(bool restoredSnapshot, bool restoreFailure)
    {
        Env.WaitMs = 20_000;
        var grain = await FreshAsync();
        if (restoredSnapshot)
        {
            Assert.True((await grain.GetStateAsync()).IsSuccess);
            Assert.True((await grain.PersistStateAsync()).IsSuccess);
            await grain.RequestDeactivationAsync();
            await Task.Delay(1000);
            await Env.EventStore.WriteSerializableEventsAsync([
                ToSerializable(CreateEvent(new Counted("tail"), DateTime.UtcNow.AddSeconds(-40)))]);
        }
        Env.WaitMs = 100;
        Env.GatingStore.FailRestore = restoreFailure;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var starts = new List<CatchUpStartPositionLease>();
        using var hook = CatchUpProductionTestHooks.Register(
            Sekiban.Dcb.ServiceId.DefaultServiceIdProvider.DefaultServiceId, CountProjector.MultiProjectorName,
            async (point, observation) =>
            {
                if (point != CatchUpProductionHookPoint.BackgroundEnteredGate) return;
                lock (starts) starts.Add(observation.Start!);
                if (Interlocked.Increment(ref calls) == 2)
                {
                    failed.TrySetResult();
                    throw new InvalidOperationException("one failed background attempt");
                }
                if (calls > 2) await release.Task;
            });
        try
        {
            _ = await grain.GetStateAsync();
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await PollUntilAsync(async () => !(await grain.GetStatusAsync()).IsCatchUpActive);
            Assert.NotNull((await grain.GetStatusAsync()).CatchUpTargetPosition);
            var result = await grain.GetStateAsync();
            Assert.False(result.IsSuccess);
            Assert.Contains("one failed background attempt", result.GetException().Message);
            var status = await grain.GetStatusAsync();
            Assert.Contains("one failed background attempt", status.LastBackgroundCatchUpError!);
            lock (starts)
            {
                Assert.Equal(CatchUpStartPositionSource.InferredCheckpoint, starts[^1].Source);
                Assert.NotNull(starts[^1].StartPosition);
            }
            release.TrySetResult();
            await PollUntilAsync(async () => !(await grain.GetStatusAsync()).FirstQueryCatchUpPending);
            Assert.Equal(restoredSnapshot ? 4 : 3,
                ((CountProjector)(await grain.GetStateAsync()).GetValue().Payload).Count);
        }
        finally { release.TrySetResult(); }
    }

    private static async Task PollUntilAsync(Func<Task<bool>> predicate)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(60))
        {
            if (await predicate()) return;
            await Task.Delay(100);
        }
        Assert.Fail("Timed out waiting for background settlement/fault");
    }

    private static Event CreateEvent(IEventPayload payload, DateTime timestamp) => new(
        payload, SortableUniqueId.Generate(timestamp, Guid.NewGuid()), payload.GetType().Name,
        Guid.NewGuid(), new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"), new List<string>());

    private static SerializableEvent ToSerializable(Event ev) => new(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ev.Payload, ev.Payload.GetType())),
        ev.SortableUniqueIdValue, ev.Id, ev.EventMetadata, ev.Tags.ToList(), ev.EventType);

    public record Counted(string Tag) : IEventPayload;
    public record CountResult(int Count);
    public record CountRow(int Index);

    public record CountQuery : IMultiProjectionQuery<CountProjector, CountQuery, CountResult>
    {
        public static ResultBox<CountResult> HandleQuery(CountProjector p, CountQuery q, IQueryContext c) =>
            ResultBox.FromValue(new CountResult(p.Count));
    }

    public record CountListQuery : IMultiProjectionListQuery<CountProjector, CountListQuery, CountRow>, IQueryPagingParameter
    {
        public int? PageNumber { get; init; }
        public int? PageSize { get; init; }
        public static ResultBox<IEnumerable<CountRow>> HandleFilter(CountProjector p, CountListQuery q, IQueryContext c) =>
            ResultBox.FromValue(Enumerable.Range(0, p.Count).Select(i => new CountRow(i)));
        public static ResultBox<IEnumerable<CountRow>> HandleSort(IEnumerable<CountRow> f, CountListQuery q, IQueryContext c) =>
            ResultBox.FromValue(f);
    }

    [global::Orleans.GenerateSerializer]
    public record CountProjector : IMultiProjector<CountProjector>
    {
        [global::Orleans.Id(0)]
        public int Count { get; init; }
        public static string MultiProjectorName => "g90-bw-count";
        public static string MultiProjectorVersion => "1.0.0";
        public static CountProjector GenerateInitialPayload() => new();
        public static ResultBox<CountProjector> Project(
            CountProjector payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) =>
            ev.Payload is Counted { Tag: "poison" }
                ? ResultBox.Error<CountProjector>(new InvalidOperationException("tail poison"))
                : ResultBox.FromValue(payload with { Count = payload.Count + 1 });
    }

    internal static class Env
    {
        public static int WaitMs { get; set; } = 100;
        public static DcbDomainTypes Domain { get; private set; } = BuildDomain();
        public static InMemoryEventStore EventStore { get; private set; } = new(Domain.EventTypes);
        public static RestoreFailingStore GatingStore { get; private set; } = new(new InMemoryMultiProjectionStateStore());

        public static void Reset()
        {
            WaitMs = 100;
            Domain = BuildDomain();
            EventStore = new InMemoryEventStore(Domain.EventTypes);
            GatingStore = new RestoreFailingStore(new InMemoryMultiProjectionStateStore());
        }

        private static DcbDomainTypes BuildDomain()
        {
            var eventTypes = new SimpleEventTypes();
            eventTypes.RegisterEventType<Counted>("Counted");
            var mp = new SimpleMultiProjectorTypes();
            mp.RegisterProjector<CountProjector>();
            var q = new SimpleQueryTypes();
            q.RegisterQuery<CountQuery>();
            q.RegisterListQuery<CountListQuery>();
            return new DcbDomainTypes(eventTypes, new SimpleTagTypes(), new SimpleTagProjectorTypes(),
                new SimpleTagStatePayloadTypes(), mp, q, new JsonSerializerOptions());
        }
    }

    internal sealed class RestoreFailingStore(IGenerationAwareCheckpointStore inner) : DelegatingCheckpointStore(inner), IMultiProjectionStateStore
    {
        public bool FailRestore { get; set; }
        public new Task<ResultBox<Stream>> OpenStateDataReadStreamAsync(MultiProjectionStateRecord record, CancellationToken ct = default) =>
            FailRestore ? Task.FromResult(ResultBox.Error<Stream>(new InvalidOperationException("snapshot restore unavailable")))
                : base.OpenStateDataReadStreamAsync(record, ct);
    }

    private class Configurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton<DcbDomainTypes>(Env.Domain);
                    services.AddSingleton<IEventStore>(Env.EventStore);
                    services.AddSingleton<IMultiProjectionStateStore>(Env.GatingStore);
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
                    services.AddTransient(_ => new GeneralMultiProjectionActorOptions { SafeWindowMs = 3000, FirstQueryCatchUpMaxWaitMs = Env.WaitMs, CatchUpMaxConsecutiveFailures = 1 });
                    services.AddSekibanDcbNativeRuntime();
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("OrleansStorage")
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }
}
