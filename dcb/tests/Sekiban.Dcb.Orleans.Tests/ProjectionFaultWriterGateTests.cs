using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Runtime.Hosting;
using Orleans.Storage;
using Orleans.TestingHost;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Testing;
using Xunit;
using DomainTypes = Sekiban.Dcb.Orleans.Tests.ProjectionFaultOrleansTests;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     Proves the single-writer gate actually serializes overlapping grain-state writes, rather than merely auditing
///     that every call site routes through it. A storage harness parks the first write inside the provider while a
///     second write becomes ready in the same grain turn; with the gate the two are serialized (max one concurrent
///     write observed), and bypassing the gate makes them overlap (two observed) — so the mutation is caught.
/// </summary>
public class ProjectionFaultWriterGateTests : IAsyncLifetime
{
    private static readonly InMemoryEventStore SharedEventStore = new();
    private TestCluster _cluster = null!;
    private IClusterClient Client => _cluster.Client;

    public async Task InitializeAsync()
    {
        SharedEventStore.Clear();
        GateProbeStorage.Reset();
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var id = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"GateCluster-{id}";
        builder.Options.ServiceId = $"GateService-{id}";
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
        {
            await _cluster.StopAllSilosAsync();
        }
    }

    [Fact]
    public async Task TwoConcurrentGrainStateWrites_AreSerializedByTheGate_MaxOneConcurrent()
    {
        var grain = Client.GetGrain<IMultiProjectionGrain>(DomainTypes.FaultTestProjector.MultiProjectorName);

        // Launch the probe (two gated writes in one turn); do NOT await it — the first write parks inside the provider.
        var probe = grain.ProbeStateWriteConcurrencyAsync();

        // Wait until the first write has entered the provider and parked.
        await GateProbeStorage.FirstParked;

        // Force a grain-turn boundary so the probe turn has definitely reached its `await WhenAll` — by then the second
        // write has already made its move (blocked on the gate, or, if bypassed, entered the provider concurrently).
        await grain.GetHealthStatusAsync();

        var maxConcurrent = GateProbeStorage.MaxConcurrent;

        // Let both writes finish.
        GateProbeStorage.Release();
        await probe;

        Assert.Equal(1, maxConcurrent);
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton(_ => DomainTypes.CreateDomain());
                    services.AddSingleton<IEventStore>(SharedEventStore);
                    services.AddSingleton<IMultiProjectionStateStore, InMemoryMultiProjectionStateStore>();
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
                    services.AddSingleton<Sekiban.Dcb.Snapshots.IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
                    services.AddTransient(_ => new GeneralMultiProjectionActorOptions { SafeWindowMs = 1 });
                    services.AddSekibanDcbNativeRuntime();
                    // The grain's [PersistentState("multiProjection", "OrleansStorage")] binds to this provider.
                    services.AddGrainStorage("OrleansStorage", (_, _) => new GateProbeStorage());
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }

    /// <summary>
    ///     Records the maximum number of writes seen concurrently inside WriteStateAsync, and parks the FIRST write on a
    ///     release the test controls so a second write in the same grain turn has a chance to overlap it. All state is
    ///     static so the test can read it across the client/silo boundary.
    /// </summary>
    private sealed class GateProbeStorage : IGrainStorage
    {
        private static readonly object Sync = new();
        private static readonly Dictionary<string, object?> Store = new();
        private static int _current;
        public static int MaxConcurrent;
        private static TaskCompletionSource _firstParked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static Task FirstParked => _firstParked.Task;
        public static void Release() => _release.TrySetResult();

        public static void Reset()
        {
            lock (Sync)
            {
                Store.Clear();
                _current = 0;
                MaxConcurrent = 0;
            }
            _firstParked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async Task WriteStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            int n;
            lock (Sync)
            {
                n = ++_current;
                if (n > MaxConcurrent)
                {
                    MaxConcurrent = n;
                }
            }

            // Park only the first writer, so a second (concurrent, if the gate is bypassed) write overlaps it here.
            if (n == 1)
            {
                _firstParked.TrySetResult();
                await _release.Task;
            }

            lock (Sync)
            {
                Store[grainId.ToString()] = grainState.State;
                _current--;
            }
        }

        public Task ReadStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            lock (Sync)
            {
                if (Store.TryGetValue(grainId.ToString(), out var saved) && saved is T typed)
                {
                    grainState.State = typed;
                    grainState.RecordExists = true;
                }
                else
                {
                    grainState.RecordExists = false;
                }
            }

            return Task.CompletedTask;
        }

        public Task ClearStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            lock (Sync)
            {
                Store.Remove(grainId.ToString());
            }

            return Task.CompletedTask;
        }
    }
}
