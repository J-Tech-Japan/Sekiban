using Sekiban.Core.Events;
namespace ShippingContext.Aggregates.Products.Events;

public record ProductCreated : IEventPayload<Product, ProductCreated>
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public decimal Price { get; init; }

    public static Product OnEvent(Product payload, Event<ProductCreated> ev) => new(ev.Payload.Name, ev.Payload.Code, ev.Payload.Price);
}
