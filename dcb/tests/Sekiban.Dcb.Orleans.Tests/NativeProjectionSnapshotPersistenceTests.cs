using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Runtime.Native;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public class NativeProjectionSnapshotPersistenceTests
{
    [Fact]
    public async Task WriteSnapshotForPersistenceToStreamAsync_Should_Offload_Large_Payload_And_Restore()
    {
        var blobAccessor = new InMemoryBlobStorageSnapshotAccessor();
        var services = new ServiceCollection()
            .AddSingleton<IBlobStorageSnapshotAccessor>(blobAccessor)
            .BuildServiceProvider();
        var domainTypes = BuildDomainTypes();
        var primitive = new NativeMultiProjectionProjectionPrimitive(domainTypes);

        var host = new NativeProjectionActorHost(
            domainTypes,
            services,
            primitive,
            LargePayloadProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 1000 },
            NullLogger.Instance);

        var events = Enumerable.Range(0, 6)
            .Select(i => CreateSerializableEvent(new LargePayloadCreated(GenerateRandomString(2048)), DateTime.UtcNow.AddSeconds(-30 + i)))
            .ToList();
        await host.AddSerializableEventsAsync(events, finishedCatchUp: true);
        host.ForcePromoteAllBufferedEvents();

        await using var snapshotStream = new MemoryStream();
        var writeResult = await host.WriteSnapshotForPersistenceToStreamAsync(
            snapshotStream,
            canGetUnsafeState: false,
            offloadThresholdBytes: 16,
            CancellationToken.None);

        Assert.True(writeResult.IsSuccess);
        snapshotStream.Position = 0;
        var envelope = await JsonSerializer.DeserializeAsync<SerializableMultiProjectionStateEnvelope>(
            snapshotStream,
            domainTypes.JsonSerializerOptions);
        Assert.NotNull(envelope);
        Assert.True(envelope!.IsOffloaded);
        Assert.NotNull(envelope.OffloadedState);

        snapshotStream.Position = 0;
        var restoredHost = new NativeProjectionActorHost(
            domainTypes,
            services,
            primitive,
            LargePayloadProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 1000 },
            NullLogger.Instance);
        var restoreResult = await restoredHost.RestoreSnapshotFromStreamAsync(snapshotStream, CancellationToken.None);
        Assert.True(restoreResult.IsSuccess);

        var stateResult = await restoredHost.GetStateAsync(canGetUnsafeState: true);
        Assert.True(stateResult.IsSuccess);
        var payload = Assert.IsType<LargePayloadProjector>(stateResult.GetValue().Payload);
        Assert.Equal(6, payload.Items.Count);
    }

    private static DcbDomainTypes BuildDomainTypes()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<LargePayloadCreated>("LargePayloadCreated");

        var tagTypes = new SimpleTagTypes();
        var tagProjectorTypes = new SimpleTagProjectorTypes();
        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjector<LargePayloadProjector>();

        return new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            tagStatePayloadTypes,
            multiProjectorTypes,
            new SimpleQueryTypes());
    }

    private static SerializableEvent CreateSerializableEvent(IEventPayload payload, DateTime at)
    {
        var sortableUniqueId = SortableUniqueId.Generate(at, Guid.NewGuid());
        return new SerializableEvent(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, payload.GetType())),
            sortableUniqueId,
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "tester"),
            [],
            payload.GetType().Name);
    }

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

    private record LargePayloadCreated(string Text) : IEventPayload;

    private record LargePayloadProjector(List<string> Items) : IMultiProjector<LargePayloadProjector>
    {
        public LargePayloadProjector() : this(new List<string>()) { }
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "native-large-payload";
        public static LargePayloadProjector GenerateInitialPayload() => new(new List<string>());

        public static ResultBox<LargePayloadProjector> Project(
            LargePayloadProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => ev.Payload switch
            {
                LargePayloadCreated created => ResultBox.FromValue(
                    payload with { Items = payload.Items.Concat([created.Text]).ToList() }),
                _ => ResultBox.FromValue(payload)
            };
    }
}
