using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes;

public class ClosedSubAggregate : ProcessingSubAggregate, IAggregatePayloadGeneratable<ClosedSubAggregate>
{
    public static ClosedSubAggregate CreateInitialPayload(ClosedSubAggregate? _) => new();
}
