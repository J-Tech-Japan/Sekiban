using Dcb.Domain.WithoutResult.Student;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.TestSupport;
using System.Text;
using Xunit;
namespace Sekiban.Dcb.WithoutResult.Tests.Actors;

/// <summary>
///     The WithoutResult facade half of the projection-fault contract (issue #1075): the fault machinery lives in the
///     shared Core, so the exception-based facade must surface a faulted replay exactly as the ResultBox facade does —
///     as a failure that reaches a boundary, not as an empty successful projection.
/// </summary>
public class ProjectionFaultFacadeParityTests
{
    private static DcbDomainTypes Domain() =>
        DcbDomainTypesExtensions.Simple(builder =>
        {
            builder.EventTypes.RegisterEventType<StudentCreated>();
            builder.EventTypes.RegisterEventType<FixtureStudentCreated>(MalformedEventPayloadFixture.EventTypeName);
            builder.MultiProjectorTypes.RegisterProjector<StudentSummaries>();
        });

    private static SerializableEvent Poison() =>
        new(
            Encoding.UTF8.GetBytes(MalformedEventPayloadFixture.TopLevelPascalCase),
            SortableUniqueId.Generate(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), Guid.Empty),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            new EventMetadata("c", "co", "u"),
            [],
            MalformedEventPayloadFixture.EventTypeName);

    [Fact]
    public async Task PoisonReplay_SurfacesError_ThroughTheSharedInMemoryAccessor()
    {
        var accessor = new InMemoryObjectAccessor(new PoisonReadEventStore(), Domain());

        var actorResult = await accessor.GetActorAsync<GeneralMultiProjectionActor>(StudentSummaries.MultiProjectorName);

        Assert.False(actorResult.IsSuccess);
    }

    [Fact]
    public async Task AFaultedActor_FailsGetState_ForTheWithoutResultDomainToo()
    {
        var actor = new GeneralMultiProjectionActor(Domain(), StudentSummaries.MultiProjectorName);
        await Assert.ThrowsAnyAsync<Exception>(() => actor.AddSerializableEventsAsync([Poison()]));

        var state = await actor.GetStateAsync();
        Assert.False(state.IsSuccess);
        Assert.IsType<SekibanProjectionFaultException>(state.GetException());
    }

    private sealed class PoisonReadEventStore : IEventStore
    {
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since = null) =>
            Task.FromResult(ResultBox.FromValue<IEnumerable<SerializableEvent>>([Poison()]));

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
