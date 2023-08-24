using Sekiban.Core.Aggregate;
namespace ShippingContext.Aggregates.Products;

public record Product(string Name, string Code, decimal Price) : IAggregatePayload<Product>
{
    public Product() : this(string.Empty, string.Empty, 0)
    {
    }
    public static Product CreateInitialPayload(Product? _) => new();
}
