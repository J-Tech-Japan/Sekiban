using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes;

public record ActiveBFAggregate : BaseFirstAggregate, IAggregateSubtypePayload<BaseFirstAggregate, ActiveBFAggregate>
{
    public static ActiveBFAggregate CreateInitialPayload(ActiveBFAggregate? _) => new();
}
