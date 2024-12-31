using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
namespace Pure.Domain;

public class ShoppingCartProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev) =>
        (payload, ev.GetPayload()) switch
        {
            (EmptyAggregatePayload, ShoppingCartCreated created) => new BuyingShoppingCart(
                created.UserId,
                new List<ShoppingCartItems>()),
            (BuyingShoppingCart buyingShoppingCart, ShoppingCartItemAdded added) => new BuyingShoppingCart(
                buyingShoppingCart.UserId,
                buyingShoppingCart
                    .Items
                    .Append(new ShoppingCartItems(added.Name, added.Quantity, Guid.NewGuid(), 100))
                    .ToList()),
            (BuyingShoppingCart buyingShoppingCart, PaymentProcessedShoppingCart processed) =>
                new EmptyAggregatePayload(),
            _ => payload
        };
}
