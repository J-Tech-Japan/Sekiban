using Dcb.ImmutableModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.ImmutableModels.Events.Student;

// DCB Pattern: One command produces ONE event that represents a business fact.
// Events can be tagged with multiple entities to affect their states.

// Entity Creation Events (single entity affected)
public record StudentCreated(Guid StudentId, string Name, int MaxClassCount = 5) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new StudentTag(StudentId));
}
