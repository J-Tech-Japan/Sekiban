using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.PurchasedCarts;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts.Events;

public record OrderSubmittedI : IEventPayload<ShoppingCartI, PurchasedCartI, OrderSubmittedI>
{
    public DateTime OrderSubmittedLocalTime { get; init; }
    public PurchasedCartI OnEventInstance(ShoppingCartI aggregatePayload, Event<OrderSubmittedI> ev)
    {
        return OnEvent(aggregatePayload, ev);
    }

    public static PurchasedCartI OnEvent(ShoppingCartI aggregatePayload, Event<OrderSubmittedI> ev)
    {
        return new()
            { Items = aggregatePayload.Items, PurchasedDate = ev.Payload.OrderSubmittedLocalTime };
    }
}
