using Dcb.ImmutableModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.ImmutableModels.Events.Enrollment;

public record StudentDroppedFromClassRoom(Guid StudentId, Guid ClassRoomId) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new StudentTag(StudentId), new ClassRoomTag(ClassRoomId));
}
