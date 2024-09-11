using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.DeletebleBoxes;

public record BoxDeleted : IEventPayload<Box, BoxDeleted>
{
    public static Box OnEvent(Box aggregatePayload, Event<BoxDeleted> _) => aggregatePayload with
    {
        IsDeleted = true
    };
}
