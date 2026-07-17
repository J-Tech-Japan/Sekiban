using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Runtime.Hosting;
using Orleans.Storage;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Testing;
using System.Collections.Generic;
using Xunit;
using DomainTypes = Sekiban.Dcb.Orleans.Tests.ProjectionFaultOrleansTests;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     The restart-durability contract when the fault-descriptor write fails: the grain must recover on its own. The
///     injected storage rejects the FIRST fault-carrying write and accepts the rest, so the product-owned retry (no
///     manual persist) makes the descriptor durable; then a genuine fresh activation restores it and the first
///     state/scalar/list queries all fail. While the descriptor is not yet durable the faulted activation stays
///     pinned, so no window opens where a fresh activation answers success.
/// </summary>
public class ProjectionFaultPersistFailureTests : IAsyncLifetime
{
    private static readonly InMemoryEventStore SharedEventStore = new();
    private TestCluster _cluster = null!;
    private ISekibanExecutor _executor = null!;
    private IClusterClient Client => _cluster.Client;

    public async Task InitializeAsync()
    {
        SharedEventStore.Clear();
        FaultWriteInjectingStorage.Reset();
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var id = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"PersistFailCluster-{id}";
        builder.Options.ServiceId = $"PersistFailService-{id}";
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
        _executor = new OrleansDcbExecutor(Client, SharedEventStore, DomainTypes.CreateDomain());
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
        {
            await _cluster.StopAllSilosAsync();
        }
    }

    [Fact]
    public async Task FirstDescriptorWriteFails_ProductRetryMakesItDurable_FreshActivationFailsClosed()
    {
        var grain = Client.GetGrain<IMultiProjectionGrain>(DomainTypes.FaultTestProjector.MultiProjectorName);
        await grain.SeedEventsAsync(
            new List<SerializableEvent> { DomainTypes.Event(poison: true, tick: 7_000) });

        // RefreshAsync folds the poison and faults; the first fault-descriptor write is rejected by the storage.
        await grain.RefreshAsync();
        Assert.True(FaultWriteInjectingStorage.RejectedWrites >= 1, "the first fault-descriptor write was not rejected");

        // On the pinned activation, every query still fails.
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
        Assert.False((await _executor.QueryAsync(new DomainTypes.FaultCountQuery())).IsSuccess);
        Assert.False((await _executor.QueryAsync(new DomainTypes.FaultRowListQuery())).IsSuccess);

        // The product-owned retry timer persists the descriptor with no manual PersistStateAsync. Wait for a durable
        // fault-descriptor write to land.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (FaultWriteInjectingStorage.DurableWrites == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
        }
        Assert.True(FaultWriteInjectingStorage.DurableWrites >= 1, "the retry never durably persisted the descriptor");

        // Force a genuine fresh activation. The descriptor is durable now, so it restores and the FIRST queries fail —
        // no empty-success window.
        await grain.RequestDeactivationAsync();
        await Task.Delay(1000);

        var reactivated = Client.GetGrain<IMultiProjectionGrain>(DomainTypes.FaultTestProjector.MultiProjectorName);
        Assert.False((await reactivated.GetSnapshotJsonAsync()).IsSuccess);
        Assert.False((await _executor.QueryAsync(new DomainTypes.FaultCountQuery())).IsSuccess);
        Assert.False((await _executor.QueryAsync(new DomainTypes.FaultRowListQuery())).IsSuccess);
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
                    services.AddGrainStorage("OrleansStorage", (sp, name) => new FaultWriteInjectingStorage());
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }

    /// <summary>
    ///     Persists real state (in memory) so a reactivation can read it back, but REJECTS the first write that carries
    ///     a fault descriptor and accepts the rest — modelling a transient store failure that recovers. Ordinary
    ///     checkpoint writes always succeed so catch-up can reach and fold the poison.
    /// </summary>
    private sealed class FaultWriteInjectingStorage : IGrainStorage
    {
        private static readonly Dictionary<string, object?> Store = new();
        private static readonly object Gate = new();
        public static int RejectedWrites;
        public static int DurableWrites;

        public static void Reset()
        {
            lock (Gate)
            {
                Store.Clear();
                RejectedWrites = 0;
                DurableWrites = 0;
            }
        }

        public Task ReadStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            lock (Gate)
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

        public Task WriteStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            var faultEventId = grainState.State?.GetType().GetProperty("FaultEventId")?.GetValue(grainState.State);
            lock (Gate)
            {
                if (faultEventId is not null && RejectedWrites == 0)
                {
                    RejectedWrites++;
                    throw new InvalidOperationException("injected: first fault-descriptor write failure");
                }

                Store[grainId.ToString()] = grainState.State;
                if (faultEventId is not null)
                {
                    DurableWrites++;
                }
            }

            return Task.CompletedTask;
        }

        public Task ClearStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            lock (Gate)
            {
                Store.Remove(grainId.ToString());
            }

            return Task.CompletedTask;
        }
    }
}
