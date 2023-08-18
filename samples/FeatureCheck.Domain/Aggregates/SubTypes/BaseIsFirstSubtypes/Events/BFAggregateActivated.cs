using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Events;

public record BFAggregateActivated : IEventPayload<BaseFirstAggregate, ActiveBFAggregate, BFAggregateActivated>
{
    public static ActiveBFAggregate OnEvent(BaseFirstAggregate aggregatePayload, Event<BFAggregateActivated> ev) =>
        new() { Name = aggregatePayload.Name, Price = aggregatePayload.Price };
}
