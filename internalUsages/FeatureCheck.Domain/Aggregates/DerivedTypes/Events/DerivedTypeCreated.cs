using FeatureCheck.Domain.Aggregates.DerivedTypes.ValueObjects;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.DerivedTypes.Events;

public record DerivedTypeCreated(IVehicle Vehicle) : IEventPayload<DerivedTypeAggregate, DerivedTypeCreated>
{
    public static DerivedTypeAggregate OnEvent(DerivedTypeAggregate aggregatePayload, Event<DerivedTypeCreated> ev) =>
        new(ev.Payload.Vehicle);
}
