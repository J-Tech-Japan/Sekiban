using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShoppingCarts.Events;

public record ItemAddedToShoppingCartR : IEventPayload<ShoppingCartR, ItemAddedToShoppingCartR>
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Quantity { get; init; } = 0;
    public ShoppingCartR OnEventInstance(ShoppingCartR aggregatePayload, Event<ItemAddedToShoppingCartR> ev)
    {
        return OnEvent(aggregatePayload, ev);
    }

    public static ShoppingCartR OnEvent(ShoppingCartR aggregatePayload, Event<ItemAddedToShoppingCartR> ev)
    {
        return aggregatePayload with
        {
            Items = aggregatePayload.Items.Add(
                aggregatePayload.Items.Count,
                new CartItemRecordR
                {
                    Code = ev.Payload.Code,
                    Name = ev.Payload.Name,
                    Quantity = ev.Payload.Quantity
                })
        };
    }
}
