using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes;

public record ClosedBFAggregate : BaseFirstAggregate, IAggregateSubtypePayload<BaseFirstAggregate, ClosedBFAggregate>
{
    public static ClosedBFAggregate CreateInitialPayload(ClosedBFAggregate? _) => new();
}
