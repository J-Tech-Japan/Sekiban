using DcbLib.Tags;

namespace DcbLib.Events;

public record EventPayloadWithTags(
    IEventPayload Event,
    List<ITag> Tags
);