using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Events;

public record BFAggregateReopened : IEventPayload<ClosedBFAggregate, BaseFirstAggregate, BFAggregateReopened>
{
    public static BaseFirstAggregate OnEvent(ClosedBFAggregate aggregatePayload, Event<BFAggregateReopened> ev) => new()
    {
        Name = aggregatePayload.Name, Price = aggregatePayload.Price
    };
}
