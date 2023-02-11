using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts.Events;

public record ItemAddedToShoppingCartI : IEventPayload<ShoppingCartI, ItemAddedToShoppingCartI>
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Quantity { get; init; } = 0;
    public ShoppingCartI OnEventInstance(ShoppingCartI aggregatePayload, Event<ItemAddedToShoppingCartI> ev)
    {
        return OnEvent(aggregatePayload, ev);
    }

    public static ShoppingCartI OnEvent(ShoppingCartI aggregatePayload, Event<ItemAddedToShoppingCartI> ev)
    {
        return aggregatePayload with
        {
            Items = aggregatePayload.Items.Add(
                aggregatePayload.Items.Count,
                new CartItemRecordI
                {
                    Code = ev.Payload.Code,
                    Name = ev.Payload.Name,
                    Quantity = ev.Payload.Quantity
                })
        };
    }
}
