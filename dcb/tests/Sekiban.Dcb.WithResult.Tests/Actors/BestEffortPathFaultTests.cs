using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Xunit;
namespace Sekiban.Dcb.WithResult.Tests.Actors;

/// <summary>
///     Acceptance (h): the in-memory subscription and publisher are best-effort — they swallow their own delivery
///     errors so one bad subscriber cannot break the others. That swallow must NOT be able to HIDE an authoritative
///     projection failure. Driving a poison event through each of them faults the underlying projection, and the fault
///     surfaces on the next read exactly as it would through catch-up — the best-effort layer swallows the throw, not
///     the fault.
/// </summary>
public class BestEffortPathFaultTests
{
    private static DcbDomainTypes Domain() =>
        DcbDomainTypesExtensions.Simple(builder =>
        {
            builder.EventTypes.RegisterEventType<Ping>();
            builder.MultiProjectorTypes.RegisterProjector<ThrowingProjector>();
        });

    private static Event Poison() =>
        new(
            new Ping(true),
            SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()),
            typeof(Ping).FullName!,
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            new EventMetadata("c", "co", "u"),
            []);

    [Fact]
    public async Task Publisher_SwallowsTheThrow_ButTheFaultStillSurfacesOnQuery()
    {
        var accessor = new InMemoryObjectAccessor(new EmptyStore(), Domain());

        // Cache a healthy projection actor.
        var actorResult = await accessor.GetActorAsync<GeneralMultiProjectionActor>(ThrowingProjector.MultiProjectorName);
        Assert.True(actorResult.IsSuccess);
        var actor = actorResult.GetValue();

        // The publisher folds the poison into the cached actor and swallows the resulting throw (best-effort).
#pragma warning disable CS0618 // exercising the best-effort in-memory publisher on purpose
        var publisher = new InMemoryMultiProjectionEventPublisher(accessor);
#pragma warning restore CS0618
        await publisher.PublishAsync([(Poison(), Array.Empty<ITag>())]); // does not throw — swallowed

        // ...but the actor is faulted, and the query says so. The swallow hid nothing authoritative.
        var state = await actor.GetStateAsync();
        Assert.False(state.IsSuccess);
        Assert.IsType<SekibanProjectionFaultException>(state.GetException());
    }

    [Fact]
    public async Task Subscription_SwallowsTheThrow_ButTheFaultStillSurfacesOnQuery()
    {
        var actor = new GeneralMultiProjectionActor(Domain(), ThrowingProjector.MultiProjectorName);

#pragma warning disable CS0618 // exercising the best-effort in-memory subscription on purpose
        var subscription = new InMemoryEventSubscription();
        await subscription.SubscribeAsync(
            async evt => await actor.AddEventsAsync([evt], finishedCatchUp: true, EventSource.Stream));
#pragma warning restore CS0618

        // Delivering the poison faults the actor; the subscription swallows the throw so other subscribers survive.
        await subscription.PublishEventAsync(Poison()); // does not throw

        var state = await actor.GetStateAsync();
        Assert.False(state.IsSuccess);
        Assert.IsType<SekibanProjectionFaultException>(state.GetException());
    }

    private record Ping(bool Poison) : IEventPayload;

    private record ThrowingProjector : IMultiProjector<ThrowingProjector>
    {
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "throwing-projector";
        public static ThrowingProjector GenerateInitialPayload() => new();

        public static ResultBox<ThrowingProjector> Project(
            ThrowingProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold)
        {
            if (ev.Payload is Ping { Poison: true })
            {
                throw new InvalidOperationException("poison: this projector refuses to fold it");
            }

            return ResultBox.FromValue(payload);
        }
    }

    private sealed class EmptyStore : IEventStore
    {
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since = null) =>
            Task.FromResult(ResultBox.FromValue<IEnumerable<SerializableEvent>>([]));
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount) => ReadAllSerializableEventsAsync(since);
        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) =>
            throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) =>
            throw new NotSupportedException();
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) =>
            throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null) => throw new NotSupportedException();
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) => throw new NotSupportedException();
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => throw new NotSupportedException();
    }
}
