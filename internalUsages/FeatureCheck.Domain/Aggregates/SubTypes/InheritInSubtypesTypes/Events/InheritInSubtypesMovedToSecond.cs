using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes.Events;

public record InheritInSubtypesMovedToSecond(int SecondProperty)
    : IEventPayload<FirstStage, SecondStage, InheritInSubtypesMovedToSecond>
{
    public static SecondStage OnEvent(FirstStage aggregatePayload, Event<InheritInSubtypesMovedToSecond> ev) =>
        new(aggregatePayload.FirstProperty, ev.Payload.SecondProperty);
}
