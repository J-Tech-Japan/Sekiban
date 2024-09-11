using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.DeletebleBoxes;

public record BoxCreated(string Code, string Name) : IEventPayload<Box, BoxCreated>
{
    public static Box OnEvent(Box aggregatePayload, Event<BoxCreated> ev) => aggregatePayload with
    {
        Code = ev.Payload.Code,
        Name = ev.Payload.Name
    };
}
