using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes;

public class ProcessingSubAggregate : IAggregateSubtypePayload<IInheritedAggregate, ProcessingSubAggregate>, IInheritedAggregate
{
    public int YearMonth { get; init; }
    public static ProcessingSubAggregate CreateInitialPayload(ProcessingSubAggregate? _) => new();
}
