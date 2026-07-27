using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Storage;
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
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     SEK-G18 single-cluster durable-rebuild-marker + checkpoint-exactness integration tests.
///     (5b) A no-progress restart preserves SafeWindowThreshold and EventsProcessed EXACTLY and performs ZERO external
///     checkpoint writes (verified with a write-counting external store).
/// </summary>
public class DurableRebuildMarkerAndRestartTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private IClusterClient _client => _cluster.Client;

    public async Task InitializeAsync()
    {
        Shared.Reset();
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"G18-marker-{uniqueId}";
        builder.Options.ServiceId = $"G18-marker-{uniqueId}";
        builder.AddSiloBuilderConfigurator<Configurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }

    [Fact]
    public async Task NoProgressRestart_PreservesThresholdAndCount_Exactly_WithZeroExternalWrites()
    {
        var grain = _client.GetGrain<IMultiProjectionGrain>(CountProjector.MultiProjectorName);

        // One event, well outside the safe window (immediately safe). Seed + refresh + persist a checkpoint.
        var evt = ToSerializable(CreateEvent(new Counted("a"), DateTime.UtcNow.AddSeconds(-30)));
        await Shared.EventStore.WriteSerializableEventsAsync(new[] { evt });
        await grain.RefreshAsync();
        Assert.True((await grain.PersistStateAsync()).IsSuccess);

        var afterFirst = await Shared.StateStore.GetLatestForVersionAsync(CountProjector.MultiProjectorName, "1.0.0");
        Assert.True(afterFirst.GetValue().HasValue);
        var recordA = afterFirst.GetValue().GetValue();

        // Restart the grain (deactivate → reactivate = a fresh activation restoring the persisted checkpoint), with NO new
        // events written in between. Count external writes from this point.
        await grain.RequestDeactivationAsync();
        await Task.Delay(1000);
        Shared.StateStore.ResetWriteCount();

        var grain2 = _client.GetGrain<IMultiProjectionGrain>(CountProjector.MultiProjectorName);
        await grain2.RefreshAsync();                 // restore + catch-up (nothing new)
        _ = await grain2.PersistStateAsync();        // a persist attempt on the unchanged safe checkpoint

        // Zero external writes/upserts (the unchanged safe checkpoint is not re-persisted).
        Assert.Equal(0, Shared.StateStore.WriteCount);

        // And the durable row is byte-identical: same SafeWindowThreshold and same EventsProcessed as before the restart.
        var afterRestore = await Shared.StateStore.GetLatestForVersionAsync(CountProjector.MultiProjectorName, "1.0.0");
        var recordB = afterRestore.GetValue().GetValue();
        Assert.Equal(recordA.SafeWindowThreshold, recordB.SafeWindowThreshold);
        Assert.Equal(recordA.EventsProcessed, recordB.EventsProcessed);
        Assert.Equal(recordA.LastSortableUniqueId, recordB.LastSortableUniqueId);
    }

    private static Event CreateEvent(IEventPayload payload, DateTime timestamp) => new(
        payload,
        SortableUniqueId.Generate(timestamp, Guid.NewGuid()),
        payload.GetType().Name,
        Guid.NewGuid(),
        new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
        new List<string>());

    private static SerializableEvent ToSerializable(Event ev) => new(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ev.Payload, ev.Payload.GetType())),
        ev.SortableUniqueIdValue, ev.Id, ev.EventMetadata, ev.Tags.ToList(), ev.EventType);

    public record Counted(string Tag) : IEventPayload;

    [global::Orleans.GenerateSerializer]
    public record CountProjector : IMultiProjector<CountProjector>
    {
        [global::Orleans.Id(0)]
        public int Count { get; init; }
        public static string MultiProjectorName => "g18-marker-count";
        public static string MultiProjectorVersion => "1.0.0";
        public static CountProjector GenerateInitialPayload() => new();
        public static ResultBox<CountProjector> Project(
            CountProjector payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) =>
            ResultBox.FromValue(payload with { Count = payload.Count + 1 });
    }

    // A write-counting external checkpoint store that decorates the in-memory store.
    internal sealed class CountingStateStore : IMultiProjectionStateStore
    {
        private readonly InMemoryMultiProjectionStateStore _inner = new();
        private int _writeCount;
        public int WriteCount => _writeCount;
        public void ResetWriteCount() => Interlocked.Exchange(ref _writeCount, 0);

        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(
            string projectorName, string projectorVersion, CancellationToken ct = default) =>
            _inner.GetLatestForVersionAsync(projectorName, projectorVersion, ct);
        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(
            string projectorName, CancellationToken ct = default) => _inner.GetLatestAnyVersionAsync(projectorName, ct);
        public Task<ResultBox<bool>> UpsertAsync(MultiProjectionStateRecord record, int offloadThresholdBytes = 1_000_000, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _writeCount);
            return _inner.UpsertAsync(record, offloadThresholdBytes, ct);
        }
        public Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(CancellationToken ct = default) => _inner.ListAllAsync(ct);
        public Task<ResultBox<bool>> DeleteAsync(string projectorName, string projectorVersion, CancellationToken ct = default) =>
            _inner.DeleteAsync(projectorName, projectorVersion, ct);
        public Task<ResultBox<int>> DeleteAllAsync(string? projectorName = null, CancellationToken ct = default) => _inner.DeleteAllAsync(projectorName, ct);
        public Task<ResultBox<Stream>> OpenStateDataReadStreamAsync(MultiProjectionStateRecord record, CancellationToken ct = default) =>
            _inner.OpenStateDataReadStreamAsync(record, ct);
        public Task<ResultBox<bool>> UpsertFromStreamAsync(MultiProjectionStateWriteRequest request, Stream stream, int offloadThresholdBytes, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _writeCount);
            return _inner.UpsertFromStreamAsync(request, stream, offloadThresholdBytes, ct);
        }
    }

    internal static class Shared
    {
        public static DcbDomainTypes Domain { get; private set; } = BuildDomain();
        public static InMemoryEventStore EventStore { get; private set; } = new(Domain.EventTypes);
        public static CountingStateStore StateStore { get; private set; } = new();

        public static void Reset()
        {
            Domain = BuildDomain();
            EventStore = new InMemoryEventStore(Domain.EventTypes);
            StateStore = new CountingStateStore();
        }

        private static DcbDomainTypes BuildDomain()
        {
            var eventTypes = new SimpleEventTypes();
            eventTypes.RegisterEventType<Counted>("Counted");
            var multiProjectorTypes = new SimpleMultiProjectorTypes();
            multiProjectorTypes.RegisterProjector<CountProjector>();
            return new DcbDomainTypes(
                eventTypes, new SimpleTagTypes(), new SimpleTagProjectorTypes(), new SimpleTagStatePayloadTypes(),
                multiProjectorTypes, new SimpleQueryTypes(), new JsonSerializerOptions());
        }
    }

    private class Configurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton<DcbDomainTypes>(Shared.Domain);
                    services.AddSingleton<IEventStore>(Shared.EventStore);
                    services.AddSingleton<IMultiProjectionStateStore>(Shared.StateStore);
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
                    services.AddTransient(_ => new GeneralMultiProjectionActorOptions { SafeWindowMs = 3000 });
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
