using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Events;

public record BFAggregateClosed : IEventPayload<ActiveBFAggregate, ClosedBFAggregate, BFAggregateClosed>
{
    public static ClosedBFAggregate OnEvent(ActiveBFAggregate aggregatePayload, Event<BFAggregateClosed> ev) => new()
    {
        Name = aggregatePayload.Name, Price = aggregatePayload.Price
    };
}
