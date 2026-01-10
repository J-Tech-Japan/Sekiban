using Dcb.ImmutableModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.ImmutableModels.Events.ClassRoom;

public record ClassRoomCreated(Guid ClassRoomId, string Name, int MaxStudents = 10) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new ClassRoomTag(ClassRoomId));
}
