using Mixed.Domain;
using Sekiban.Testing.SingleProjections;
using ShippingContext.Aggregates.Products;
using ShippingContext.Aggregates.Products.Commands;
namespace Mixed.Test;

public class ProductSpec : AggregateTest<Product, MixedContextDependency>
{
    [Fact]
    public void Test1()
    {
        WhenCommand(new CreateProduct { Code = "001", Name = "Product1", Price = 100 })
            .ThenNotThrowsAnException()
            .ThenPayloadIs(new Product("Product1", "001", 100));
    }
}
