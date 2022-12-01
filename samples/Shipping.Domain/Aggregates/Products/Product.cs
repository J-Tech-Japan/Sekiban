using Sekiban.Core.Aggregate;

namespace ShippingContext.Aggregates.Products;

public record Product(string Name, string Code, decimal Price) : IAggregatePayload
{
    public Product() : this(string.Empty, string.Empty, 0)
    {
    }
}
