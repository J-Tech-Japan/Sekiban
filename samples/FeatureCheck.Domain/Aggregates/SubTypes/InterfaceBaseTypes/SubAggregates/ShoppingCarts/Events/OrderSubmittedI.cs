using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.SubAggregates.PurchasedCarts;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.SubAggregates.ShoppingCarts.Events;

public record OrderSubmittedI : IEventPayload<ShoppingCartI, PurchasedCartI, OrderSubmittedI>
{
    public DateTime OrderSubmittedLocalTime { get; init; }

    public static PurchasedCartI OnEvent(ShoppingCartI aggregatePayload, Event<OrderSubmittedI> ev) => new()
        { Items = aggregatePayload.Items, PurchasedDate = ev.Payload.OrderSubmittedLocalTime };
    public PurchasedCartI OnEventInstance(ShoppingCartI aggregatePayload, Event<OrderSubmittedI> ev) => OnEvent(aggregatePayload, ev);
}
