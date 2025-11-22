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
    private readonly GeneralMultiProjectionActorOptions _options;

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

        _options = new GeneralMultiProjectionActorOptions { SafeWindowMs = 1000 };
    }

    [Fact]
    public async Task Snapshot_Inline_When_Below_Threshold()
    {
        var actor = new GeneralMultiProjectionActor(_domainTypes, BigPayloadProjector.MultiProjectorName, _options);
        var small = new Created("small");
        await actor.AddEventsAsync(new[] { Ev(small) });

        var accessor = new InMemoryBlobStorageSnapshotAccessor();
        // Even if accessor is set, size below threshold keeps inline
        var actorWithPolicy = new GeneralMultiProjectionActor(
            _domainTypes,
            BigPayloadProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions
            {
                SafeWindowMs = _options.SafeWindowMs,
                SnapshotAccessor = accessor,
                SnapshotOffloadThresholdBytes = 1024 * 1024
            });
        // Re-apply same event to the configured actor
        await actorWithPolicy.AddEventsAsync(new[] { Ev(small) });
        var envelope = await actorWithPolicy.GetSnapshotAsync(false);

        Assert.True(envelope.IsSuccess);
        var env = envelope.GetValue();
        Assert.False(env.IsOffloaded);
        Assert.NotNull(env.InlineState);
    }

    [Fact]
    public async Task Snapshot_Offloads_When_Above_Threshold()
    {
        var actor = new GeneralMultiProjectionActor(_domainTypes, BigPayloadProjector.MultiProjectorName, _options);
        // Add a few events with relatively large strings to exceed a tiny threshold
        foreach (var i in Enumerable.Range(0, 5))
        {
            var created = new Created(GenerateRandomString(1024));
            await actor.AddEventsAsync(new[] { Ev(created) });
        }

        var accessor = new InMemoryBlobStorageSnapshotAccessor();
        // Configure actor with offload policy
        var actorWithPolicy = new GeneralMultiProjectionActor(
            _domainTypes,
            BigPayloadProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions
            {
                SafeWindowMs = _options.SafeWindowMs,
                SnapshotAccessor = accessor,
                SnapshotOffloadThresholdBytes = 500
            });

        // Re-apply same events to actorWithPolicy
        foreach (var i in Enumerable.Range(0, 5))
        {
            var created = new Created(GenerateRandomString(1024));
            await actorWithPolicy.AddEventsAsync(new[] { Ev(created) });
        }

        var envelope = await actorWithPolicy.GetSnapshotAsync(true);

        Assert.True(envelope.IsSuccess);
        var env = envelope.GetValue();
        Assert.True(env.IsOffloaded);
        Assert.NotNull(env.OffloadedState);
        Assert.Equal(accessor.ProviderName, env.OffloadedState!.StorageProvider);

        // Verify that payload can be fetched back from storage
        var bytes = await accessor.ReadAsync(env.OffloadedState.OffloadKey);
        Assert.Equal(env.OffloadedState.PayloadLength, bytes.LongLength);

        // Restore to a fresh actor using SetSnapshotAsync
        var restored = new GeneralMultiProjectionActor(
            _domainTypes,
            BigPayloadProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions
            {
                SafeWindowMs = _options.SafeWindowMs,
                SnapshotAccessor = accessor,
                SnapshotOffloadThresholdBytes = 500
            });
        await restored.SetSnapshotAsync(env);
        var state = await restored.GetStateAsync(false);
        Assert.True(state.IsSuccess);
        var proj = (BigPayloadProjector)state.GetValue().Payload;
        Assert.Equal(5, proj.Items.Count);
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
