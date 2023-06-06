using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.PurchasedCarts;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShoppingCarts.Events;

public record OrderSubmittedR : IEventPayload<ShoppingCartR, PurchasedCartR, OrderSubmittedR>
{
    public DateTime OrderSubmittedLocalTime { get; init; }
    public PurchasedCartR OnEventInstance(ShoppingCartR aggregatePayload, Event<OrderSubmittedR> ev) => OnEvent(aggregatePayload, ev);

    public static PurchasedCartR OnEvent(ShoppingCartR aggregatePayload, Event<OrderSubmittedR> ev) =>
        new() { Items = aggregatePayload.Items, PurchasedDate = ev.Payload.OrderSubmittedLocalTime };
}
