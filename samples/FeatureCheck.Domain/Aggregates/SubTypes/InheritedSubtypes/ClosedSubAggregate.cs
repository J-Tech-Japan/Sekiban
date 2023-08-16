using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes;

public class ClosedSubAggregate : ProcessingSubAggregate
{
    public new static IAggregatePayloadCommon CreateInitialPayload() => new ClosedSubAggregate();
}
