using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.SubAggregates.ShoppingCarts.Events;

public record ItemAddedToShoppingCartI : IEventPayload<ShoppingCartI, ItemAddedToShoppingCartI>
{

    public static ShoppingCartI OnEvent(ShoppingCartI aggregatePayload, Event<ItemAddedToShoppingCartI> ev) => throw new NotImplementedException();
    public ShoppingCartI OnEventInstance(ShoppingCartI aggregatePayload, Event<ItemAddedToShoppingCartI> ev) => OnEvent(aggregatePayload, ev);
}
