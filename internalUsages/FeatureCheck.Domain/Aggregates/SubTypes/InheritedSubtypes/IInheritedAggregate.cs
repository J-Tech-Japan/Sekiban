using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes;

public interface IInheritedAggregate : IParentAggregatePayload<IInheritedAggregate, ProcessingSubAggregate>
{
}
