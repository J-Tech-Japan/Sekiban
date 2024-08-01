using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes.Events;

public record InheritInSubTypesCreated(int FirstProperty) : IEventPayload<FirstStage, InheritInSubTypesCreated>
{
    public static FirstStage OnEvent(FirstStage aggregatePayload, Event<InheritInSubTypesCreated> ev) => new(ev.Payload.FirstProperty);
}
