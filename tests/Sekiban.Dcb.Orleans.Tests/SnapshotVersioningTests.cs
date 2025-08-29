using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Text.Json;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public class SnapshotVersioningTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private IClusterClient _client => _cluster.Client;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"SnapshotTests-Cluster-{uniqueId}";
        builder.Options.ServiceId = $"SnapshotTests-Service-{uniqueId}";
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }

    [Fact]
    public async Task OnActivate_Should_Restore_From_Snapshot_When_Version_Matches()
    {
        var grainId = CounterProjector.MultiProjectorName;
        var grain = _client.GetGrain<IMultiProjectionGrain>(grainId);

        // Create events in event store via grain helper
        var events = Enumerable.Range(0, 5).Select(i => CreateEvent(new TestEvt($"e{i}"), DateTime.UtcNow.AddSeconds(-30 + i))).ToList();
        await grain.SeedEventsAsync(events);

        // Catch up and persist snapshot
        await grain.RefreshAsync();
        var persist = await grain.PersistStateAsync();
        Assert.True(persist.IsSuccess);

        // Deactivate and reactivate (to trigger restore path)
        await grain.RequestDeactivationAsync();
        var grain2 = _client.GetGrain<IMultiProjectionGrain>(grainId);

        var serStateAfter = await grain2.GetSnapshotJsonAsync(canGetUnsafeState: false);
        Assert.True(serStateAfter.IsSuccess);
        var env = JsonSerializer.Deserialize<Sekiban.Dcb.Snapshots.SerializableMultiProjectionStateEnvelope>(serStateAfter.GetValue());
        var version = env.IsOffloaded ? env.OffloadedState!.Version : env.InlineState!.Version;
        Assert.Equal(5, version);
    }

    [Fact]
    public async Task OnActivate_Should_Fallback_When_Version_Mismatch()
    {
        var grainId = CounterProjector.MultiProjectorName;
        var grain = _client.GetGrain<IMultiProjectionGrain>(grainId);

        // Create events in event store via grain helper
        var events = Enumerable.Range(0, 7).Select(i => CreateEvent(new TestEvt($"x{i}"), DateTime.UtcNow.AddSeconds(-30 + i))).ToList();
        await grain.SeedEventsAsync(events);

        // Catch up and persist snapshot
        await grain.RefreshAsync();
        var persist = await grain.PersistStateAsync();
        Assert.True(persist.IsSuccess);

        // Corrupt the stored snapshot version to simulate mismatch
        var overwritten = await grain.OverwritePersistedStateVersionAsync("9.9.9");
        Assert.True(overwritten);

        // Deactivate and reactivate
        await grain.RequestDeactivationAsync();
        var grain2 = _client.GetGrain<IMultiProjectionGrain>(grainId);

        // Should not throw; should rebuild from event store and reach 7
        var serStateAfter = await grain2.GetSnapshotJsonAsync(canGetUnsafeState: false);
        Assert.True(serStateAfter.IsSuccess);
        var env = JsonSerializer.Deserialize<Sekiban.Dcb.Snapshots.SerializableMultiProjectionStateEnvelope>(serStateAfter.GetValue());
        var version = env.IsOffloaded ? env.OffloadedState!.Version : env.InlineState!.Version;
        Assert.Equal(7, version);
    }

    private static Event CreateEvent(IEventPayload payload, DateTime when)
    {
        var sortableId = SortableUniqueId.Generate(when, Guid.NewGuid());
        return new Event(
            payload,
            sortableId,
            payload.GetType().Name,
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "TestUser"),
            new List<string>());
    }

    private class TestSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton<DcbDomainTypes>(provider =>
                    {
                        var eventTypes = new SimpleEventTypes();
                        var tagTypes = new SimpleTagTypes();
                        var tagProjectorTypes = new SimpleTagProjectorTypes();
                        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
                        var multiProjectorTypes = new SimpleMultiProjectorTypes();
                        var queryTypes = new SimpleQueryTypes();

                        multiProjectorTypes.RegisterProjector<CounterProjector>();

                        return new DcbDomainTypes(
                            eventTypes,
                            tagTypes,
                            tagProjectorTypes,
                            tagStatePayloadTypes,
                            multiProjectorTypes,
                            queryTypes,
                            new JsonSerializerOptions());
                    });

                    services.AddSingleton<IEventStore, InMemoryEventStore>();
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("OrleansStorage")
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }

    // Simple event payload for tests
    private record TestEvt(string Name) : IEventPayload;

    // Projector counting processed events (safe/unsafe window agnostic for simplicity)
    private record CounterProjector(int Count) : IMultiProjector<CounterProjector>
    {
        public CounterProjector() : this(0) {}
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "snap-proj";
        public static CounterProjector GenerateInitialPayload() => new(0);
        public static ResultBox<CounterProjector> Project(
            CounterProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload with { Count = payload.Count + 1 });
    }
}
