using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes;

public class ProcessingSubAggregate : IAggregateSubtypePayload<IInheritedAggregate>, IInheritedAggregate
{
    public int YearMonth { get; init; }
    public static IAggregatePayloadCommon CreateInitialPayload() => new ProcessingSubAggregate();
}
