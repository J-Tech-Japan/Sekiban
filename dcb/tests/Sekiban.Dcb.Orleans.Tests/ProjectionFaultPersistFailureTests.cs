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
///     The fail-closed half of the durability contract, with descriptor persistence deterministically broken. When the
///     grain-state write that carries the fault descriptor throws, the grain must NOT discard the faulted activation
///     into a fresh empty one — that would lose the only record of the fault and reopen the first-query empty success.
///     Instead the faulted activation is pinned and keeps failing every query surface. Ordinary checkpoint writes here
///     succeed (so catch-up can reach and fold the poison); only the fault-carrying write fails.
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
        WriteFailingGrainStorage.FaultWriteAttempts = 0;
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
    public async Task PersistenceFailure_PinsTheFaultedActivation_NoQuerySurfaceSucceeds()
    {
        var grain = Client.GetGrain<IMultiProjectionGrain>(DomainTypes.FaultTestProjector.MultiProjectorName);
        await grain.SeedEventsAsync(
            new List<SerializableEvent> { DomainTypes.Event(poison: true, tick: 7_000) });
        await grain.RefreshAsync();

        // The fault-carrying write is rejected by the injected storage.
        var persisted = await grain.PersistStateAsync();
        Assert.False(persisted.IsSuccess);
        Assert.True(WriteFailingGrainStorage.FaultWriteAttempts > 0, "the fault-descriptor write was never attempted");

        // Every query surface still fails on the pinned, live-faulted activation — the persistence failure did not
        // reopen a success.
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
        Assert.False((await _executor.QueryAsync(new DomainTypes.FaultCountQuery())).IsSuccess);
        Assert.False((await _executor.QueryAsync(new DomainTypes.FaultRowListQuery())).IsSuccess);

        // And a later query — after the grain had every chance to be reclaimed — still fails: the faulted activation
        // was not silently discarded into an empty one.
        await Task.Delay(1500);
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
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
                    // The grain's [PersistentState("multiProjection", "OrleansStorage")] binds to this provider. Only
                    // the fault-carrying write throws; ordinary checkpoint writes succeed, so catch-up still reaches
                    // and folds the poison.
                    services.AddGrainStorage("OrleansStorage", (sp, name) => new WriteFailingGrainStorage());
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }

    /// <summary>Reads return empty state; the write that carries a fault descriptor throws; other writes are accepted.</summary>
    private sealed class WriteFailingGrainStorage : IGrainStorage
    {
        public static int FaultWriteAttempts;

        public Task ReadStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            grainState.RecordExists = false;
            return Task.CompletedTask;
        }

        public Task WriteStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            var faultEventId = grainState.State?.GetType().GetProperty("FaultEventId")?.GetValue(grainState.State);
            if (faultEventId is not null)
            {
                Interlocked.Increment(ref FaultWriteAttempts);
                throw new InvalidOperationException("injected: fault-descriptor write failure");
            }

            return Task.CompletedTask;
        }

        public Task ClearStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState) =>
            Task.CompletedTask;
    }
}
