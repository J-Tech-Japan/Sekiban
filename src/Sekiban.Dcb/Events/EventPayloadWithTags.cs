using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Events;

public record EventPayloadWithTags(IEventPayload Event, List<ITagCommon> Tags);
