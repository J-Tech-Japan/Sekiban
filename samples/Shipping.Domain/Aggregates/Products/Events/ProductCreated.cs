using Sekiban.Core.Event;
namespace ShippingContext.Aggregates.Products.Events;

public record ProductCreated : IEventPayload<Product>
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public Product OnEvent(Product payload, IEvent ev) => new(Name, Code, Price);
}
