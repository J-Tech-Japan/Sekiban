using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;

namespace AspireEventSample.ApiService.Aggregates.Carts;

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
                    .Append(new ShoppingCartItems(added.Name, added.Quantity, added.ItemId, added.Price))
                    .ToList()),
            (BuyingShoppingCart buyingShoppingCart, ShoppingCartPaymentProcessed processed) =>
                new PaymentProcessingShoppingCart(buyingShoppingCart.UserId, buyingShoppingCart.Items, processed.PaymentMethod),
            _ => payload
        };
}