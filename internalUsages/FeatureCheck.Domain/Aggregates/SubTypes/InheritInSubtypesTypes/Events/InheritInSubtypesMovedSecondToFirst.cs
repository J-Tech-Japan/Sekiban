using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes.Events;

public record
    InheritInSubtypesMovedSecondToFirst : IEventPayload<SecondStage, FirstStage, InheritInSubtypesMovedSecondToFirst>
{
    public static FirstStage OnEvent(SecondStage aggregatePayload, Event<InheritInSubtypesMovedSecondToFirst> ev) =>
        new(aggregatePayload.FirstProperty);
}
