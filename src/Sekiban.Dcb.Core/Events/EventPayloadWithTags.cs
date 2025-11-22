using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Events;

public record EventPayloadWithTags(IEventPayload Event, params List<ITag> Tags);
