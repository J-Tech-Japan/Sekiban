using FeatureCheck.Domain.Aggregates.DerivedTypes.ValueObjects;
using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.DerivedTypes;

public record DerivedTypeAggregate(IVehicle Vehicle) : IAggregatePayload<DerivedTypeAggregate>
{
    public static DerivedTypeAggregate CreateInitialPayload(DerivedTypeAggregate? _) => new(Car.Empty);
}
