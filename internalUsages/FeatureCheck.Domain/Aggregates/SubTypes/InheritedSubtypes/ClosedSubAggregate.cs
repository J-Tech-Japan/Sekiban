using FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes;
using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes;

public class ClosedSubAggregate : ProcessingSubAggregate,
    IAggregateSubtypePayload<IInheritedAggregate, ClosedSubAggregate>,
    IAggregateSubtypePayload<BaseFirstAggregate, ClosedSubAggregate>
{
    public static ClosedSubAggregate CreateInitialPayload(ClosedSubAggregate? _) => new();
}
