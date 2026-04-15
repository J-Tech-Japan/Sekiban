using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Text;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     Integration tests for the streaming persist pipeline.
///     Verifies that MultiProjectionGrain can persist and restore snapshots
///     using the stream/temp-file based path (UseStreamingSnapshotIO = true).
/// </summary>
public class StreamingPersistIntegrationTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private IClusterClient _client => _cluster.Client;

    public async Task InitializeAsync()
    {
        StreamingPersistSiloConfigurator.SharedBlobAccessor.Clear();
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"StreamingPersist-Cluster-{uniqueId}";
        builder.Options.ServiceId = $"StreamingPersist-Service-{uniqueId}";
        builder.AddSiloBuilderConfigurator<StreamingPersistSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }

    [Fact]
    public async Task PersistStateAsync_With_StreamingIO_Should_Succeed()
    {
        // Given: grain with events processed
        var grainId = CounterProjector.MultiProjectorName;
        var grain = _client.GetGrain<IMultiProjectionGrain>(grainId);

        var events = Enumerable.Range(0, 5)
            .Select(i => CreateEvent(new StreamingTestEvt($"evt{i}"), DateTime.UtcNow.AddSeconds(-30 + i)))
            .ToList();
        await grain.SeedEventsAsync(ToSerializableEvents(events));
        await grain.RefreshAsync();

        // When: persisting with streaming IO enabled
        var result = await grain.PersistStateAsync();

        // Then: persist succeeds
        Assert.True(result.IsSuccess);
        Assert.True(result.GetValue());
    }

    [Fact]
    public async Task StreamingPersist_Should_Restore_After_Deactivation()
    {
        // Given: grain has persisted state via streaming path
        var grainId = CounterProjector.MultiProjectorName;
        var grain = _client.GetGrain<IMultiProjectionGrain>(grainId);

        var events = Enumerable.Range(0, 7)
            .Select(i => CreateEvent(new StreamingTestEvt($"s{i}"), DateTime.UtcNow.AddSeconds(-30 + i)))
            .ToList();
        await grain.SeedEventsAsync(ToSerializableEvents(events));
        await grain.RefreshAsync();

        var persistResult = await grain.PersistStateAsync();
        Assert.True(persistResult.IsSuccess);

        // When: deactivate and re-acquire
        await grain.RequestDeactivationAsync();
        await Task.Delay(1000);

        var grain2 = _client.GetGrain<IMultiProjectionGrain>(grainId);
        await Task.Delay(500);

        // Then: snapshot is restored with correct version
        var snapshotJson = await grain2.GetSnapshotJsonAsync(canGetUnsafeState: false);
        Assert.True(snapshotJson.IsSuccess);

        var env = JsonSerializer.Deserialize<SerializableMultiProjectionStateEnvelope>(
            snapshotJson.GetValue());
        Assert.NotNull(env);

        var version = env!.IsOffloaded
            ? env.OffloadedState!.Version
            : env.InlineState!.Version;
        Assert.Equal(7, version);
    }

    [Fact]
    public async Task StreamingPersist_Should_Preserve_State_Integrity()
    {
        // Given: grain with events, persisted via streaming
        var grainId = CounterProjector.MultiProjectorName;
        var grain = _client.GetGrain<IMultiProjectionGrain>(grainId);

        var events = Enumerable.Range(0, 3)
            .Select(i => CreateEvent(new StreamingTestEvt($"int{i}"), DateTime.UtcNow.AddSeconds(-30 + i)))
            .ToList();
        await grain.SeedEventsAsync(ToSerializableEvents(events));
        await grain.RefreshAsync();

        // Get pre-persist snapshot
        var prePersistSnap = await grain.GetSnapshotJsonAsync(canGetUnsafeState: false);
        Assert.True(prePersistSnap.IsSuccess);

        // Persist
        var persistResult = await grain.PersistStateAsync();
        Assert.True(persistResult.IsSuccess);

        // Deactivate and re-acquire
        await grain.RequestDeactivationAsync();
        await Task.Delay(1000);

        var grain2 = _client.GetGrain<IMultiProjectionGrain>(grainId);
        await Task.Delay(500);

        // Then: post-restore snapshot has same version and structure
        var postRestoreSnap = await grain2.GetSnapshotJsonAsync(canGetUnsafeState: false);
        Assert.True(postRestoreSnap.IsSuccess);

        var prePersistEnv = JsonSerializer.Deserialize<SerializableMultiProjectionStateEnvelope>(
            prePersistSnap.GetValue());
        var postRestoreEnv = JsonSerializer.Deserialize<SerializableMultiProjectionStateEnvelope>(
            postRestoreSnap.GetValue());

        Assert.NotNull(prePersistEnv);
        Assert.NotNull(postRestoreEnv);

        // Version count should match
        var preVersion = prePersistEnv!.InlineState?.Version ?? prePersistEnv.OffloadedState?.Version ?? 0;
        var postVersion = postRestoreEnv!.InlineState?.Version ?? postRestoreEnv.OffloadedState?.Version ?? 0;
        Assert.Equal(preVersion, postVersion);

        // ProjectorName should match
        var preName = prePersistEnv.InlineState?.ProjectorName ?? prePersistEnv.OffloadedState?.ProjectorName;
        var postName = postRestoreEnv.InlineState?.ProjectorName ?? postRestoreEnv.OffloadedState?.ProjectorName;
        Assert.Equal(preName, postName);
    }

    [Fact]
    public async Task StreamingPersist_Should_Offload_Large_Payload_Before_Restore()
    {
        var grainId = LargePayloadProjector.MultiProjectorName;
        var grain = _client.GetGrain<IMultiProjectionGrain>(grainId);
        StreamingPersistSiloConfigurator.SharedBlobAccessor.Clear();

        var events = Enumerable.Range(0, 6)
            .Select(i => CreateEvent(new LargeStreamingTestEvt(GenerateRandomString(2048)), DateTime.UtcNow.AddSeconds(-30 + i)))
            .ToList();
        await grain.SeedEventsAsync(ToSerializableEvents(events));
        await grain.RefreshAsync();

        var persistResult = await grain.PersistStateAsync();
        Assert.True(persistResult.IsSuccess);
        Assert.True(persistResult.GetValue());

        await grain.RequestDeactivationAsync();
        await Task.Delay(1000);

        var grain2 = _client.GetGrain<IMultiProjectionGrain>(grainId);
        await Task.Delay(500);

        var snapshotJson = await grain2.GetSnapshotJsonAsync(canGetUnsafeState: false);
        Assert.True(snapshotJson.IsSuccess);

        var env = JsonSerializer.Deserialize<SerializableMultiProjectionStateEnvelope>(snapshotJson.GetValue());
        Assert.NotNull(env);
        var version = env!.InlineState?.Version ?? env.OffloadedState?.Version ?? 0;
        Assert.Equal(6, version);
        Assert.True(StreamingPersistSiloConfigurator.SharedBlobAccessor.StoredObjectCount > 0);
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

    private static IReadOnlyList<SerializableEvent> ToSerializableEvents(IEnumerable<Event> events) =>
        events
            .Select(e => new SerializableEvent(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
                e.SortableUniqueIdValue,
                e.Id,
                e.EventMetadata,
                e.Tags.ToList(),
                e.EventType))
            .ToList();

    private record StreamingTestEvt(string Name) : IEventPayload;
    private record LargeStreamingTestEvt(string Text) : IEventPayload;

    private static string GenerateRandomString(int size)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var buffer = new char[size];
        for (var i = 0; i < size; i++)
        {
            buffer[i] = chars[Random.Shared.Next(chars.Length)];
        }
        return new string(buffer);
    }

    private record LargePayloadProjector(List<string> Items) : IMultiProjector<LargePayloadProjector>
    {
        public LargePayloadProjector() : this(new List<string>()) { }
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "large-streaming-payload";
        public static LargePayloadProjector GenerateInitialPayload() => new(new List<string>());

        public static ResultBox<LargePayloadProjector> Project(
            LargePayloadProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => ev.Payload switch
            {
                LargeStreamingTestEvt large => ResultBox.FromValue(
                    payload with { Items = payload.Items.Concat([large.Text]).ToList() }),
                _ => ResultBox.FromValue(payload)
            };
    }

    /// <summary>
    ///     Silo configurator that enables UseStreamingSnapshotIO = true.
    /// </summary>
    private class StreamingPersistSiloConfigurator : ISiloConfigurator
    {
        internal static MockBlobStorageSnapshotAccessor SharedBlobAccessor { get; } = new();

        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton<DcbDomainTypes>(provider =>
                    {
                        var eventTypes = new SimpleEventTypes();
                        eventTypes.RegisterEventType<StreamingTestEvt>();
                        eventTypes.RegisterEventType<LargeStreamingTestEvt>();
                        var tagTypes = new SimpleTagTypes();
                        var tagProjectorTypes = new SimpleTagProjectorTypes();
                        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
                        var multiProjectorTypes = new SimpleMultiProjectorTypes();
                        var queryTypes = new SimpleQueryTypes();

                        multiProjectorTypes.RegisterProjectorWithCustomSerialization<CounterProjector>();
                        multiProjectorTypes.RegisterProjector<LargePayloadProjector>();

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
                    services.AddSingleton<IMultiProjectionStateStore, InMemoryMultiProjectionStateStore>();
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IBlobStorageSnapshotAccessor>(SharedBlobAccessor);
                    services.AddTransient<Sekiban.Dcb.MultiProjections.IMultiProjectionEventStatistics,
                        Sekiban.Dcb.MultiProjections.NoOpMultiProjectionEventStatistics>();

                    // Enable streaming snapshot IO
                    services.AddTransient<GeneralMultiProjectionActorOptions>(_ =>
                        new GeneralMultiProjectionActorOptions
                        {
                            SafeWindowMs = 20000,
                            UseStreamingSnapshotIO = true,
                            MaxSnapshotSerializedSizeBytes = 512
                        });

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
