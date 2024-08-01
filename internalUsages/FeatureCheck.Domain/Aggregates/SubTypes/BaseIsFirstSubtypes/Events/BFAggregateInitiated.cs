using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Events;

public record BFAggregateInitiated(string Name, long Price) : IEventPayload<BaseFirstAggregate, BFAggregateInitiated>
{
    public static BaseFirstAggregate OnEvent(BaseFirstAggregate aggregatePayload, Event<BFAggregateInitiated> ev) => new()
    {
        Name = ev.Payload.Name, Price = ev.Payload.Price
    };
}
