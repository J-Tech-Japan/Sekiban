using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Tests;

public class ActorSnapshotOffloadTests
{
    private readonly DcbDomainTypes _domainTypes;

    public ActorSnapshotOffloadTests()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<Created>("Created");

        var tagTypes = new SimpleTagTypes();
        var tagProjectorTypes = new SimpleTagProjectorTypes();
        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();

        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjector<BigPayloadProjector>();

        _domainTypes = new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            tagStatePayloadTypes,
            multiProjectorTypes,
            new SimpleQueryTypes());

    }

    [Fact]
    public async Task Snapshot_Is_Inline_For_Small_Payload()
    {
        var actor = new GeneralMultiProjectionActor(
            _domainTypes,
            BigPayloadProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 1000 });
        var small = new Created("small");
        await actor.AddEventsAsync(new[] { Ev(small) });

        var envelope = await actor.GetSnapshotAsync(false);

        Assert.True(envelope.IsSuccess);
        var env = envelope.GetValue();
        Assert.False(env.IsOffloaded);
        Assert.NotNull(env.InlineState);
    }

    [Fact]
    public async Task Snapshot_Is_Inline_For_Large_Payload()
    {
        var actor = new GeneralMultiProjectionActor(
            _domainTypes,
            BigPayloadProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 1000 });
        // Add a few events with relatively large strings to exceed a tiny threshold
        foreach (var i in Enumerable.Range(0, 5))
        {
            var created = new Created(GenerateRandomString(1024));
            await actor.AddEventsAsync(new[] { Ev(created) });
        }

        var envelope = await actor.GetSnapshotAsync(true);

        Assert.True(envelope.IsSuccess);
        var env = envelope.GetValue();
        Assert.False(env.IsOffloaded);
        Assert.NotNull(env.InlineState);
    }

    [Fact]
    public async Task BuildSnapshotEnvelopeAsync_Should_Offload_Large_Payload_And_Remain_Restorable()
    {
        var actor = new GeneralMultiProjectionActor(
            _domainTypes,
            BigPayloadProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 1000 });
        var blobAccessor = new InMemoryBlobStorageSnapshotAccessor();

        foreach (var i in Enumerable.Range(0, 6))
        {
            var created = new Created(GenerateRandomString(2048));
            await actor.AddEventsAsync(new[] { Ev(created) });
        }

        var envelopeResult = await actor.BuildSnapshotEnvelopeAsync(
            canGetUnsafeState: true,
            blobAccessor: blobAccessor,
            offloadThresholdBytes: 512);

        Assert.True(envelopeResult.IsSuccess);
        var envelope = envelopeResult.GetValue();
        Assert.True(envelope.IsOffloaded);
        Assert.Null(envelope.InlineState);
        Assert.NotNull(envelope.OffloadedState);

        var resolved = await SnapshotEnvelopeResolver.ResolveInlineAsync(envelope, blobAccessor);
        Assert.False(resolved.IsOffloaded);
        Assert.NotNull(resolved.InlineState);

        var restoredActor = new GeneralMultiProjectionActor(
            _domainTypes,
            BigPayloadProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 1000 });
        await restoredActor.SetSnapshotAsync(resolved);

        var stateResult = await restoredActor.GetStateAsync(canGetUnsafeState: true);
        Assert.True(stateResult.IsSuccess);
        var restoredPayload = Assert.IsType<BigPayloadProjector>(stateResult.GetValue().Payload);
        Assert.Equal(6, restoredPayload.Items.Count);
    }

    [Fact]
    public async Task BuildSnapshotEnvelopeStreamFirstAsync_Should_Offload_Large_Payload()
    {
        var actor = new GeneralMultiProjectionActor(
            _domainTypes,
            BigPayloadProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 1000 });
        var blobAccessor = new InMemoryBlobStorageSnapshotAccessor();
        var bufferProvider = new SpillableSnapshotPayloadBufferProvider(
            new SpillableSnapshotPayloadOptions { InMemoryThresholdBytes = 256 });

        foreach (var i in Enumerable.Range(0, 6))
        {
            var created = new Created(GenerateRandomString(2048));
            await actor.AddEventsAsync(new[] { Ev(created) });
        }

        var envelopeResult = await actor.BuildSnapshotEnvelopeStreamFirstAsync(
            bufferProvider,
            canGetUnsafeState: true,
            blobAccessor: blobAccessor,
            offloadThresholdBytes: 512);

        Assert.True(envelopeResult.IsSuccess);
        var envelope = envelopeResult.GetValue();
        Assert.True(envelope.IsOffloaded);
        Assert.Null(envelope.InlineState);
        Assert.NotNull(envelope.OffloadedState);
        Assert.True(envelope.OffloadedState!.PayloadLength > 0);
        Assert.True(envelope.OffloadedState.CompressedSizeBytes > 0);

        // Restore the state from the offloaded snapshot and verify it matches the original.
        var resolved = await SnapshotEnvelopeResolver.ResolveInlineAsync(envelope, blobAccessor);
        Assert.False(resolved.IsOffloaded);
        Assert.NotNull(resolved.InlineState);

        var restoredActor = new GeneralMultiProjectionActor(
            _domainTypes,
            BigPayloadProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 1000 });
        await restoredActor.SetSnapshotAsync(resolved);

        var stateResult = await restoredActor.GetStateAsync(canGetUnsafeState: true);
        Assert.True(stateResult.IsSuccess);
        var restoredPayload = Assert.IsType<BigPayloadProjector>(stateResult.GetValue().Payload);
        Assert.Equal(6, restoredPayload.Items.Count);
    }

    [Fact]
    public async Task BuildSnapshotEnvelopeStreamFirstAsync_Should_Inline_Small_Payload()
    {
        var actor = new GeneralMultiProjectionActor(
            _domainTypes,
            BigPayloadProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 1000 });
        var blobAccessor = new InMemoryBlobStorageSnapshotAccessor();
        var bufferProvider = new SpillableSnapshotPayloadBufferProvider(
            new SpillableSnapshotPayloadOptions { InMemoryThresholdBytes = 1024 * 1024 });

        // Small payload, well under the offload threshold.
        await actor.AddEventsAsync(new[] { Ev(new Created("tiny")) });

        var envelopeResult = await actor.BuildSnapshotEnvelopeStreamFirstAsync(
            bufferProvider,
            canGetUnsafeState: true,
            blobAccessor: blobAccessor,
            offloadThresholdBytes: 1024 * 1024);

        Assert.True(envelopeResult.IsSuccess);
        var envelope = envelopeResult.GetValue();
        Assert.False(envelope.IsOffloaded);
        Assert.NotNull(envelope.InlineState);
        Assert.True(envelope.InlineState!.CompressedSizeBytes > 0);
        Assert.True(envelope.InlineState.OriginalSizeBytes > 0);

        // Restore and verify contents.
        var restoredActor = new GeneralMultiProjectionActor(
            _domainTypes,
            BigPayloadProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 1000 });
        await restoredActor.SetSnapshotAsync(envelope);

        var stateResult = await restoredActor.GetStateAsync(canGetUnsafeState: true);
        Assert.True(stateResult.IsSuccess);
        var restoredPayload = Assert.IsType<BigPayloadProjector>(stateResult.GetValue().Payload);
        Assert.Single(restoredPayload.Items);
        Assert.Equal("tiny", restoredPayload.Items[0]);
    }

    [Fact]
    public async Task BuildSnapshotEnvelopeStreamFirstAsync_Should_Spill_Large_Payload_To_TempFile()
    {
        var actor = new GeneralMultiProjectionActor(
            _domainTypes,
            BigPayloadProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 1000 });
        var blobAccessor = new InMemoryBlobStorageSnapshotAccessor();

        // InMemoryThresholdBytes deliberately tiny so the stream has to spill to disk
        // for any non-trivial projection — this is the behavior under test.
        var tempDir = Path.Combine(Path.GetTempPath(), $"sekiban-spill-test-{Guid.NewGuid():N}");
        var bufferProvider = new SpillableSnapshotPayloadBufferProvider(
            new SpillableSnapshotPayloadOptions
            {
                InMemoryThresholdBytes = 16,
                TempDirectory = tempDir
            });

        try
        {
            foreach (var i in Enumerable.Range(0, 6))
            {
                var created = new Created(GenerateRandomString(2048));
                await actor.AddEventsAsync(new[] { Ev(created) });
            }

            var envelopeResult = await actor.BuildSnapshotEnvelopeStreamFirstAsync(
                bufferProvider,
                canGetUnsafeState: true,
                blobAccessor: blobAccessor,
                offloadThresholdBytes: 512);

            Assert.True(envelopeResult.IsSuccess);
            var envelope = envelopeResult.GetValue();
            Assert.True(envelope.IsOffloaded);
            Assert.NotNull(envelope.OffloadedState);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task SetSnapshot_Throws_When_Offloaded()
    {
        var actor = new GeneralMultiProjectionActor(
            _domainTypes,
            BigPayloadProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 1000 });

        var offloaded = new SerializableMultiProjectionStateOffloaded(
            OffloadKey: "dummy",
            StorageProvider: "test",
            MultiProjectionPayloadType: typeof(BigPayloadProjector).FullName ?? "payload",
            ProjectorName: BigPayloadProjector.MultiProjectorName,
            ProjectorVersion: BigPayloadProjector.MultiProjectorVersion,
            LastSortableUniqueId: SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()),
            LastEventId: Guid.NewGuid(),
            Version: 1,
            IsCatchedUp: true,
            IsSafeState: true,
            PayloadLength: 10);

        var envelope = new SerializableMultiProjectionStateEnvelope(true, null, offloaded);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => actor.SetSnapshotAsync(envelope));
        Assert.Contains("Offloaded snapshots are not supported", ex.Message);
    }

    private static Event Ev(IEventPayload p)
    {
        var sortableId = SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid());
        return new Event(
            p,
            sortableId,
            p.GetType().Name,
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "User"),
            new List<string>());
    }

    public record Created(string Text) : IEventPayload;

    private static string GenerateRandomString(int size)
    {
        var rng = Random.Shared;
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var buffer = new char[size];
        for (int i = 0; i < size; i++) buffer[i] = chars[rng.Next(chars.Length)];
        return new string(buffer);
    }

    public record BigPayloadProjector(List<string> Items) : IMultiProjector<BigPayloadProjector>
    {
        public BigPayloadProjector() : this(new List<string>()) { }
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "BigPayloadProjector";
        public static BigPayloadProjector GenerateInitialPayload() => new(new List<string>());
        public static ResultBox<BigPayloadProjector> Project(
            BigPayloadProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => ev.Payload switch
            {
                Created c => ResultBox.FromValue(payload with { Items = payload.Items.Concat(new[] { c.Text }).ToList() }),
                _ => ResultBox.FromValue(payload)
            };
    }
}
