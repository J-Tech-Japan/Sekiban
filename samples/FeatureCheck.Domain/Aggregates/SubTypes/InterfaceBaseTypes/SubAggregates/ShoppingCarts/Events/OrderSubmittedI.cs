using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.SubAggregates.PurchasedCarts;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.SubAggregates.ShoppingCarts.Events;

public record OrderSubmittedI : IEventPayload<ShoppingCartI, PurchasedCartI, OrderSubmittedI>
{

    public static PurchasedCartI OnEvent(ShoppingCartI aggregatePayload, Event<OrderSubmittedI> ev) => throw new NotImplementedException();
    public PurchasedCartI OnEventInstance(ShoppingCartI aggregatePayload, Event<OrderSubmittedI> ev) => OnEvent(aggregatePayload, ev);
}
